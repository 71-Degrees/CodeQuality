/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Common;

/// <summary>Where a site's builds go. The first target is built from the latest commit (staging); a target with <see cref="PromotesFrom"/> takes the build another target is running (production takes staging's).</summary>
public sealed class ReleaseTarget
{
    public string Name { get; init; } = string.Empty;

    /// <summary>The site's version endpoint: JSON with a <c>version</c> like <c>1.0.0+4c8ee4e9</c>, the part after the plus being the commit.</summary>
    public string VersionUrl { get; init; } = string.Empty;

    /// <summary>The name of the target this one gets its build from; empty when it is built from the latest commit.</summary>
    public string? PromotesFrom { get; init; }

    /// <summary>The button's words, for example "Deploy to staging".</summary>
    public string Button { get; init; } = string.Empty;

    /// <summary>Show changes to <see cref="ReleaseSettings.SensitivePaths"/> in red before this target is deployed.</summary>
    public bool WarnOnSensitive { get; init; }
}

/// <summary>The dashboard's release panel: what is running where, how far behind it is, and a button that opens the deploy console (which asks before it does anything).</summary>
public sealed class ReleaseSettings
{
    /// <summary>Where the deploy console listens; the buttons open it.</summary>
    public string ConsoleUrl { get; init; } = string.Empty;

    public ReleaseTarget[] Targets { get; init; } = [];

    /// <summary>Path fragments (case-insensitive) of the code that decides who can do what or takes payment: a release that changes one gets a warning.</summary>
    public string[] SensitivePaths { get; init; } = [];
}
