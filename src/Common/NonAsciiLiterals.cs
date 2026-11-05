/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CodeQuality.Common;

/// <summary>
/// Finds the characters above U+007F that sit inside a literal an escape can express: a regular string, a char, or the text part of a regular interpolated string. The owner's rule
/// (2026-11-05) is to write them as <c>\u</c> escapes, which survive any tool that reads the file with the wrong encoding; a literal and its escaped form compile to the same string.
/// Verbatim strings, raw strings and comments have no escape syntax, so they are reported as skipped rather than rewritten.
/// </summary>
public static class NonAsciiLiterals
{
    /// <summary>The tokens whose text holds a non-ASCII character and can be escaped. Each token's text is the source text, so it can be spliced by span.</summary>
    public static IEnumerable<SyntaxToken> Find(SyntaxNode root) =>
        root.DescendantTokens().Where(token => IsEscapable(token) && HasNonAscii(token.Text));

    /// <summary>The places that hold non-ASCII text but cannot take an escape: verbatim and raw literals, and comments. Only for reporting.</summary>
    public static IEnumerable<(TextSpan Span, string Kind)> Skipped(SyntaxNode root)
    {
        foreach (var token in root.DescendantTokens().Where(token => HasNonAscii(token.Text) && !IsEscapable(token)))
        {
            if (SkippedKind(token) is { } kind)
            {
                yield return (token.Span, kind);
            }
        }

        foreach (var trivia in root.DescendantTrivia().Where(t => IsComment(t) && HasNonAscii(t.ToString())))
        {
            yield return (trivia.Span, "comment");
        }
    }

    /// <summary>Writes each non-ASCII character as an escape: <c>\uXXXX</c> inside the BMP, <c>\UXXXXXXXX</c> for a surrogate pair (an emoji). ASCII is left as it is.</summary>
    public static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c < 0x80)
            {
                builder.Append(c);
            }
            else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                builder.Append("\\U").Append(char.ConvertToUtf32(c, text[i + 1]).ToString("X8", CultureInfo.InvariantCulture));
                i++;
            }
            else
            {
                builder.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    public static bool HasNonAscii(string text) => text.Any(c => c > 0x7F);

    /// <summary>A regular string, a char, or the text of a regular interpolated string (not the braces around its holes).</summary>
    private static bool IsEscapable(SyntaxToken token) => token.Kind() switch
    {
        SyntaxKind.StringLiteralToken => token.Text.StartsWith('"'),
        SyntaxKind.CharacterLiteralToken => true,
        SyntaxKind.InterpolatedStringTextToken => token.Parent?.Ancestors().OfType<InterpolatedStringExpressionSyntax>().FirstOrDefault() is { StringStartToken: var start }
            && start.IsKind(SyntaxKind.InterpolatedStringStartToken),
        _ => false,
    };

    /// <summary>What kind of literal a non-escapable token is, or null for anything else (an identifier, say, which is not ours to report).</summary>
    private static string? SkippedKind(SyntaxToken token) => token.Kind() switch
    {
        SyntaxKind.StringLiteralToken => "verbatim string",
        SyntaxKind.SingleLineRawStringLiteralToken or SyntaxKind.MultiLineRawStringLiteralToken => "raw string",
        SyntaxKind.InterpolatedStringTextToken => InterpolatedKind(token),
        _ => null,
    };

    private static string? InterpolatedKind(SyntaxToken token) =>
        token.Parent?.Ancestors().OfType<InterpolatedStringExpressionSyntax>().FirstOrDefault()?.StringStartToken.Kind() switch
        {
            SyntaxKind.InterpolatedVerbatimStringStartToken => "verbatim interpolated string",
            SyntaxKind.InterpolatedSingleLineRawStringStartToken or SyntaxKind.InterpolatedMultiLineRawStringStartToken => "raw interpolated string",
            _ => null,
        };

    private static bool IsComment(SyntaxTrivia trivia) => trivia.Kind() is SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia
        or SyntaxKind.SingleLineDocumentationCommentTrivia or SyntaxKind.MultiLineDocumentationCommentTrivia;
}
