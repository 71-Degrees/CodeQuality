/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Refactor.Fixers;

/// <summary>CA1310: string comparison that uses the machine's culture by default (so "i" and "I" can compare differently on a Turkish server). The fix says what was meant: an ordinal, byte-for-byte comparison.</summary>
internal sealed class StringComparisonFixer : IFixer
{
    private static readonly string[] Methods = { "StartsWith", "EndsWith", "IndexOf", "LastIndexOf", "Compare" };

    public IReadOnlyCollection<string> Rules { get; } = new[] { "CA1310" };

    public string? RequiredUsing => null;

    public string? Fix(string text, int offset, Warning warning)
    {
        var method = SourceText.MethodNameIn(warning.Message);
        if (method is null || !Methods.Contains(method))
        {
            return null;
        }

        var call = SourceText.CallAt(SourceText.Parse(text), offset, method);
        return call is null ? null : SourceText.AppendArgument(text, call, "StringComparison.Ordinal");
    }
}
