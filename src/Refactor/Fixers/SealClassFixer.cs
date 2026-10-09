/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeQuality.Refactor.Fixers;

/// <summary>CA1852: a class nothing inherits from is marked <c>sealed</c>, which says so and lets the runtime call its methods directly.</summary>
internal sealed class SealClassFixer : IFixer
{
    private static readonly SyntaxKind[] Blocking = { SyntaxKind.SealedKeyword, SyntaxKind.AbstractKeyword, SyntaxKind.StaticKeyword };

    public IReadOnlyCollection<string> Rules { get; } = new[] { "CA1852" };

    public string? RequiredUsing => null;

    public string? Fix(string text, int offset, Warning warning)
    {
        var type = SourceText.TypeAt(SourceText.Parse(text), offset);
        if (type is null || type.Modifiers.Any(m => Blocking.Contains(m.Kind())))
        {
            return null;
        }

        // "sealed" goes before "partial" when the class has it, otherwise right before the "class" keyword.
        var anchor = type.Modifiers.FirstOrDefault(m => m.IsKind(SyntaxKind.PartialKeyword));
        var position = anchor.IsKind(SyntaxKind.None) ? type.Keyword.SpanStart : anchor.SpanStart;
        return text.Insert(position, "sealed ");
    }
}
