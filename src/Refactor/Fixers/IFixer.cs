/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Refactor.Fixers;

/// <summary>A behaviour-preserving fix for one kind of analyzer warning.</summary>
internal interface IFixer
{
    /// <summary>The analyzer rule ids this fixer handles, e.g. CA1816.</summary>
    IReadOnlyCollection<string> Rules { get; }

    /// <summary>A namespace the fixed code needs a <c>using</c> for (null when none).</summary>
    string? RequiredUsing { get; }

    /// <summary>Returns the text with the warning at <paramref name="offset"/> fixed, or null when this particular warning is not one the fixer can fix safely (the tool then leaves it for a person).</summary>
    string? Fix(string text, int offset, Warning warning);
}
