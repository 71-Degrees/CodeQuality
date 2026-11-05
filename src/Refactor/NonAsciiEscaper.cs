/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text;
using CodeQuality.Common;

namespace CodeQuality.Refactor;

/// <summary>What escaping one file did: how many literals changed, the new text, and the places left alone (verbatim, raw and comments) as <c>line: kind</c>.</summary>
internal sealed record EscapeResult(string Text, int Escaped, IReadOnlyList<string> Left);

/// <summary>
/// Writes the non-ASCII characters of the regular literals as escapes (the owner's rule, 2026-11-05). Only the spans of the literal tokens are replaced, so the rest of the file stays byte for
/// byte as it was: line endings, whitespace and any byte-order mark (which the file keeps, too).
/// </summary>
internal static class NonAsciiEscaper
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly byte[] Preamble = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble();

    /// <summary>The escaped text of one file's source. Edits run from the bottom of the file upwards, so each span still holds its place when its turn comes.</summary>
    public static EscapeResult Apply(string text)
    {
        var root = SourceText.Parse(text);
        var tokens = NonAsciiLiterals.Find(root).OrderByDescending(token => token.SpanStart).ToList();
        var edited = text;
        foreach (var token in tokens)
        {
            edited = edited[..token.SpanStart] + NonAsciiLiterals.Escape(token.Text) + edited[token.Span.End..];
        }

        var left = NonAsciiLiterals.Skipped(root)
            .OrderBy(skipped => skipped.Span.Start)
            .Select(skipped => $"{root.SyntaxTree.GetLineSpan(skipped.Span).StartLinePosition.Line + 1}: {skipped.Kind}")
            .ToList();
        return new EscapeResult(edited, tokens.Count, left);
    }

    /// <summary>Reads one file, escapes it and, when asked and something changed, writes it back in the encoding it came in (with or without its byte-order mark).</summary>
    public static EscapeResult ApplyToFile(string path, bool write)
    {
        var bytes = File.ReadAllBytes(path);
        var hasBom = bytes.Take(Preamble.Length).SequenceEqual(Preamble);
        var text = Utf8.GetString(bytes, hasBom ? Preamble.Length : 0, bytes.Length - (hasBom ? Preamble.Length : 0));
        var result = Apply(text);
        if (write && result.Escaped > 0)
        {
            File.WriteAllText(path, result.Text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: hasBom));
        }

        return result;
    }
}
