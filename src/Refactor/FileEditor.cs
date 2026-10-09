/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Refactor.Fixers;

namespace CodeQuality.Refactor;

/// <summary>What fixing one file did: the new text, the warnings that were fixed, and those left for a person.</summary>
internal sealed record FileEdit(string Text, IReadOnlyList<Warning> Fixed, IReadOnlyList<Warning> Left);

/// <summary>
/// Fixes every warning in one file. Warnings are applied from the bottom of the file upwards, so an edit never moves the position of a warning that has yet to be fixed, and each fix is made on the
/// text as it stands (so two fixes in the same statement cannot trample each other).
/// </summary>
internal static class FileEditor
{
    public static FileEdit Edit(string text, IEnumerable<Warning> warnings, IReadOnlyList<IFixer> fixers)
    {
        var fixedWarnings = new List<Warning>();
        var left = new List<Warning>();
        var usings = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var warning in warnings.OrderByDescending(w => w.Line).ThenByDescending(w => w.Column))
        {
            var fixer = fixers.FirstOrDefault(f => f.Rules.Contains(warning.Rule));
            var edited = fixer?.Fix(text, SourceText.Offset(text, warning.Line, warning.Column), warning);
            if (edited is null || fixer is null)
            {
                left.Add(warning);
                continue;
            }

            text = edited;
            fixedWarnings.Add(warning);
            if (fixer.RequiredUsing is { } required)
            {
                usings.Add(required);
            }
        }

        text = usings.Aggregate(text, Usings.Ensure);
        return new FileEdit(text, fixedWarnings, left);
    }
}
