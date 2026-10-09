/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeQuality.Refactor;

/// <summary>Adds a <c>using</c> directive to a file when a fix needs one, in the place a person would put it (with the other <c>System</c> usings, in order).</summary>
internal static class Usings
{
    public static string Ensure(string text, string @namespace)
    {
        var root = (CompilationUnitSyntax)SourceText.Parse(text);
        var existing = root.Usings.Select(u => u.Name?.ToString() ?? string.Empty).ToList();
        if (existing.Contains(@namespace))
        {
            return text;
        }

        var line = $"using {@namespace};{SourceText.NewLine(text)}";
        var system = root.Usings.Where(u => IsSystem(u)).ToList();
        var after = system.LastOrDefault(u => string.CompareOrdinal(u.Name?.ToString(), @namespace) < 0);
        var before = system.FirstOrDefault(u => string.CompareOrdinal(u.Name?.ToString(), @namespace) > 0);
        if (after is not null)
        {
            return text.Insert(after.FullSpan.End, line);
        }

        if (before is not null)
        {
            return text.Insert(before.SpanStart, line);
        }

        return root.Usings.Count > 0
            ? text.Insert(root.Usings[^1].FullSpan.End, line)
            : text.Insert(root.Members.Count > 0 ? root.Members[0].SpanStart : 0, line + SourceText.NewLine(text));
    }

    private static bool IsSystem(UsingDirectiveSyntax directive) =>
        directive.Alias is null && directive.StaticKeyword.IsKind(SyntaxKind.None) && (directive.Name?.ToString().StartsWith("System", StringComparison.Ordinal) ?? false);
}
