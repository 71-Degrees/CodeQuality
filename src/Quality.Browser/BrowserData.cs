/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Browser;

/// <summary>Where a site's browser tests keep their data and run log: <c>reports/browser-data</c> under the repository, or under the root the quality tool was told to report into (<c>QUALITY_OUTPUT_ROOT</c>), so a run from a separate copy of the code still appends to the same kept data and the same log.</summary>
public static class BrowserData
{
    /// <summary>The same variable the quality tool honours for its own outputs.</summary>
    public const string OutputRootVariable = "QUALITY_OUTPUT_ROOT";

    public static string Folder(string repositoryRoot) => Path.Combine(OutputRoot(repositoryRoot, Environment.GetEnvironmentVariable(OutputRootVariable)), "reports", "browser-data");

    /// <summary>The decision with the setting passed in, so it can be tested without touching the environment.</summary>
    public static string OutputRoot(string repositoryRoot, string? configured) => configured is { Length: > 0 } && Directory.Exists(configured) ? configured : repositoryRoot;
}
