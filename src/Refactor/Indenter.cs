/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeQuality.Refactor;

/// <summary>Moves a block of statements one level deeper without changing what it means. Lines that sit inside an ordinary multi-line string are left exactly as they are, because their leading spaces are part of the string.</summary>
internal static class Indenter
{
    public static string Indent(string block, int spaces)
    {
        var lines = block.Split('\n');
        var frozen = LinesInsideStrings(block);
        var pad = new string(' ', spaces);
        for (var i = 0; i < lines.Length; i++)
        {
            var text = lines[i].TrimEnd('\r');
            var isBlank = text.Length == 0;
            if (!isBlank && !frozen.Contains(i))
            {
                lines[i] = pad + lines[i];
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>Moves a block out by up to <paramref name="spaces"/> spaces. Lines inside an ordinary multi-line string, and lines with less indentation than that, are left alone.</summary>
    public static string Dedent(string block, int spaces)
    {
        var lines = block.Split('\n');
        var frozen = LinesInsideStrings(block);
        var pad = new string(' ', spaces);
        for (var i = 0; i < lines.Length; i++)
        {
            if (!frozen.Contains(i) && lines[i].StartsWith(pad, StringComparison.Ordinal))
            {
                lines[i] = lines[i][spaces..];
            }
        }

        return string.Join('\n', lines);
    }

    /// <summary>The zero-based lines that begin inside a multi-line verbatim or interpolated string (not a raw string, whose content shifts as one with its closing quotes).</summary>
    private static HashSet<int> LinesInsideStrings(string block)
    {
        var tree = CSharpSyntaxTree.ParseText("class C { void M() {\n" + block + "\n} }");
        var inside = new HashSet<int>();
        foreach (var token in tree.GetRoot().DescendantTokens().Where(t => IsPlainMultilineString(t)))
        {
            var span = tree.GetLineSpan(token.Span);
            for (var line = span.StartLinePosition.Line + 1; line <= span.EndLinePosition.Line; line++)
            {
                inside.Add(line - 1);   // the wrapper adds one line before the block
            }
        }

        return inside;
    }

    /// <summary>A verbatim or interpolated (not raw) string token that spans lines. Raw string literals have their own token kinds and so never match the first case.</summary>
    private static bool IsPlainMultilineString(SyntaxToken token)
    {
        if (!token.ToString().Contains('\n', StringComparison.Ordinal))
        {
            return false;
        }

        return token.IsKind(SyntaxKind.StringLiteralToken) || (token.IsKind(SyntaxKind.InterpolatedStringTextToken) && !IsInRawInterpolation(token));
    }

    private static bool IsInRawInterpolation(SyntaxToken token) =>
        token.Parent?.AncestorsAndSelf().OfType<InterpolatedStringExpressionSyntax>().FirstOrDefault()?.StringStartToken.Kind()
            is SyntaxKind.InterpolatedSingleLineRawStringStartToken or SyntaxKind.InterpolatedMultiLineRawStringStartToken;
}
