/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeQuality.Quality;

/// <summary>One C# file as a run of normalized tokens: every name becomes "id" and every literal "lit", so code that differs only in what things are called still matches.</summary>
internal sealed record TokenStream(string File, ulong[] Hashes, int[] Lines);

/// <summary>
/// Copy-and-paste detection. Every window of <see cref="Thresholds.DuplicateTokens"/> consecutive normalized tokens is hashed; a hash seen in two non-overlapping places marks those tokens as duplicated,
/// and each unbroken run of marked tokens that spans at least <see cref="Thresholds.DuplicateLines"/> lines is one finding, saying where else the same code is. This is the DRY check: one place should know each rule.
/// </summary>
internal static class Duplicates
{
    private const ulong Base = 1_099_511_628_211UL;

    /// <summary>Reads a parsed C# file into a token stream, leaving out the tokens that say nothing about behaviour.</summary>
    public static TokenStream Stream(string file, SyntaxTree tree)
    {
        var hashes = new List<ulong>();
        var lines = new List<int>();
        foreach (var token in tree.GetRoot().DescendantTokens().Where(t => !Ignored(t)))
        {
            hashes.Add(Fnv(Normalize(token)));
            lines.Add(tree.GetLineSpan(token.Span).StartLinePosition.Line + 1);
        }

        return new TokenStream(file, hashes.ToArray(), lines.ToArray());
    }

    /// <summary>The duplicated runs across all the streams.</summary>
    public static IReadOnlyList<Finding> Find(IReadOnlyList<TokenStream> streams, Thresholds limits)
    {
        var window = limits.DuplicateTokens;
        var seen = Index(streams, window);
        var covered = streams.Select(s => new bool[s.Hashes.Length]).ToArray();
        var partner = new Dictionary<(int Stream, int Position), (int Stream, int Position)>();
        foreach (var places in seen.Values.Where(p => p.Count > 1))
        {
            var distinct = NonOverlapping(places, window);
            if (distinct.Count < 2)
            {
                continue;
            }

            foreach (var place in distinct)
            {
                Array.Fill(covered[place.Stream], true, place.Position, window);
                partner[place] = distinct.First(other => other != place);
            }
        }

        return Numbered(Runs(streams, covered, partner, window, limits.DuplicateLines));
    }

    /// <summary>Two runs in one file can start with the same code; the second and later get an ordinal so each is its own entry in the baseline.</summary>
    private static List<Finding> Numbered(List<Finding> findings)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        return findings.Select(f =>
        {
            var key = f.File + "|" + f.Member;
            var n = seen[key] = seen.GetValueOrDefault(key) + 1;
            return n == 1 ? f : f with { Member = f.Member + " #" + n };
        }).ToList();
    }

    private static Dictionary<ulong, List<(int Stream, int Position)>> Index(IReadOnlyList<TokenStream> streams, int window)
    {
        var seen = new Dictionary<ulong, List<(int Stream, int Position)>>();
        var top = 1UL;
        for (var i = 1; i < window; i++)
        {
            top = unchecked(top * Base);
        }

        for (var s = 0; s < streams.Count; s++)
        {
            var tokens = streams[s].Hashes;
            if (tokens.Length < window)
            {
                continue;
            }

            var hash = 0UL;
            for (var i = 0; i < tokens.Length; i++)
            {
                hash = unchecked(hash * Base + tokens[i]);
                if (i >= window)
                {
                    hash = unchecked(hash - tokens[i - window] * top * Base);
                }

                if (i >= window - 1)
                {
                    var places = seen.TryGetValue(hash, out var existing) ? existing : seen[hash] = new List<(int, int)>();
                    places.Add((s, i - window + 1));
                }
            }
        }

        return seen;
    }

    /// <summary>The places that repeat each other for real. Windows that overlap inside one file are a single repetitive stretch (a long list of similar lines), which is not a copy of anything: such a group is dropped.</summary>
    private static List<(int Stream, int Position)> NonOverlapping(List<(int Stream, int Position)> places, int window)
    {
        var ordered = places.OrderBy(p => p.Stream).ThenBy(p => p.Position).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Stream == ordered[i - 1].Stream && ordered[i].Position - ordered[i - 1].Position < window)
            {
                return new List<(int Stream, int Position)>();
            }
        }

        return ordered;
    }

    private static List<Finding> Runs(IReadOnlyList<TokenStream> streams, bool[][] covered, Dictionary<(int Stream, int Position), (int Stream, int Position)> partner, int window, int minLines)
    {
        var findings = new List<Finding>();
        for (var s = 0; s < streams.Count; s++)
        {
            var stream = streams[s];
            for (var i = 0; i < covered[s].Length; i++)
            {
                if (!covered[s][i])
                {
                    continue;
                }

                var end = i;
                while (end + 1 < covered[s].Length && covered[s][end + 1])
                {
                    end++;
                }

                var lineCount = stream.Lines[end] - stream.Lines[i] + 1;
                if (lineCount >= minLines)
                {
                    findings.Add(Describe(streams, stream, (i, end), lineCount, partner.TryGetValue((s, i), out var other) ? other : null, window));
                }

                i = end;
            }
        }

        return findings;
    }

    private static Finding Describe(IReadOnlyList<TokenStream> streams, TokenStream stream, (int Start, int End) run, int lineCount, (int Stream, int Position)? other, int window)
    {
        var where = other is { } o ? $"{streams[o.Stream].File}:{streams[o.Stream].Lines[o.Position]}" : "another file";
        var id = Hex(stream.Hashes[run.Start] ^ (stream.Hashes[Math.Min(run.Start + window - 1, stream.Hashes.Length - 1)] * Base));
        return new Finding("DuplicateCode", stream.File, "dup " + id, stream.Lines[run.Start], lineCount,
            $"{lineCount} lines from line {stream.Lines[run.Start]} are repeated elsewhere (for example {where}): extract the shared part so one place knows the rule.");
    }

    /// <summary>
    /// Tokens that say nothing about behaviour: using directives, attributes, enum members, auto-properties (a list of `public string? X { get; set; }` looks identical
    /// in every data class), and constants with a plain literal value (a table of `public const string X = "...";` is a list of words, and with names and literals
    /// ignored every such table looks the same: two unrelated tables of sentences were once reported as one rule written twice).
    /// </summary>
    private static bool Ignored(SyntaxToken token) =>
        token.Parent?.AncestorsAndSelf().Any(n => n is UsingDirectiveSyntax or AttributeListSyntax or EnumDeclarationSyntax || IsAutoProperty(n) || IsLiteralConstant(n)) == true;

    private static bool IsLiteralConstant(SyntaxNode node) =>
        node is FieldDeclarationSyntax field
        && field.Modifiers.Any(SyntaxKind.ConstKeyword)
        && field.Declaration.Variables.All(v => v.Initializer?.Value is LiteralExpressionSyntax);

    private static bool IsAutoProperty(SyntaxNode node) =>
        node is PropertyDeclarationSyntax { ExpressionBody: null, AccessorList: { } accessors } && accessors.Accessors.All(a => a.Body is null && a.ExpressionBody is null);

    private static string Normalize(SyntaxToken token) => token.Kind() switch
    {
        SyntaxKind.IdentifierToken => "id",
        SyntaxKind.NumericLiteralToken or SyntaxKind.StringLiteralToken or SyntaxKind.CharacterLiteralToken or SyntaxKind.InterpolatedStringTextToken
            or SyntaxKind.Utf8StringLiteralToken or SyntaxKind.SingleLineRawStringLiteralToken or SyntaxKind.MultiLineRawStringLiteralToken => "lit",
        _ => token.Text,
    };

    private static ulong Fnv(string text)
    {
        var hash = 14_695_981_039_346_656_037UL;
        foreach (var ch in text)
        {
            hash = unchecked((hash ^ ch) * 1_099_511_628_211UL);
        }

        return hash;
    }

    private static string Hex(ulong value) => value.ToString("x16", System.Globalization.CultureInfo.InvariantCulture)[..8];
}
