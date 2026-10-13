/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;

namespace CodeQuality;

/// <summary>How far the feature list had got at one commit: functions listed, walked end to end by a browser, and with a browser test of any kind.</summary>
internal sealed record FeaturePoint(string Commit, string At, int Functions, int Walked, int WithBrowserTest);

/// <summary>
/// The feature list's progress over time, read back from git: the list is committed, so its numbers at every commit that changed it are in that commit's copy of the file and nothing extra has to be recorded.
/// </summary>
internal static class FeatureTrend
{
    /// <summary>A test counts as a browser test when its class name says so (by this repository's convention, <c>...BrowserTests</c>). Null when there is no usable list.</summary>
    public static (int Functions, int Walked, int WithBrowserTest)? Count(string? registryJson)
    {
        if (string.IsNullOrWhiteSpace(registryJson))
        {
            return null;
        }

        var functions = FeatureRegistryFile.Parse(registryJson)?.Functions;
        return functions is null
            ? null
            : (functions.Length, functions.Count(f => f.Walked), functions.Count(f => f.Tests.Any(t => t.Contains("Browser", StringComparison.OrdinalIgnoreCase))));
    }

    /// <summary>The numbers at each of <paramref name="commits"/> (oldest first), asking <paramref name="showRegistryAt"/> for the file's text there; a commit where it cannot be read is left out.</summary>
    public static async Task<List<FeaturePoint>> PointsAsync(IEnumerable<(string Commit, string At)> commits, Func<string, Task<string?>> showRegistryAt)
    {
        var points = new List<FeaturePoint>();
        foreach (var (commit, at) in commits)
        {
            if (Count(await showRegistryAt(commit).ConfigureAwait(false)) is var (functions, walked, browser))
            {
                points.Add(new FeaturePoint(commit, at, functions, walked, browser));
            }
        }

        return points;
    }
}
