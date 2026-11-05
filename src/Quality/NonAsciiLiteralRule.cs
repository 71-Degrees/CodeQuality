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

/// <summary>
/// One finding per string or char literal that writes a non-ASCII character directly. The owner's rule (2026-11-05): write it as a <c>\u</c> escape, because an escape survives any tool that
/// reads the file with the wrong encoding. Which literals count is decided by <see cref="NonAsciiLiterals"/>, shared with the Refactor tool that fixes them, so the rule and the fix agree.
/// </summary>
internal static class NonAsciiLiteralRule
{
    private const string Message = "non-ASCII character in a string literal: write it as \\uXXXX so it survives an encoding change (the Refactor tool's escape-non-ascii fixes it).";

    public static void Check(string file, SyntaxNode root, SyntaxTree tree, List<Finding> findings)
    {
        var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var token in NonAsciiLiterals.Find(root))
        {
            var owner = OwnerOf(token);
            ordinals[owner] = ordinals.GetValueOrDefault(owner) + 1;
            var line = tree.GetLineSpan(token.Span).StartLinePosition.Line + 1;
            findings.Add(new Finding("NonAsciiLiteral", file, $"{owner} literal@{ordinals[owner]}", line, 1, Message));
        }
    }

    /// <summary>The type the literal sits in, so the finding's name survives edits elsewhere in the file (never a line number).</summary>
    private static string OwnerOf(SyntaxToken token) =>
        token.Parent?.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "(top-level)";
}
