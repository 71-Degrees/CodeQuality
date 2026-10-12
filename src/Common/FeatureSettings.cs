/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Common;

/// <summary>What the feature map needs to know about a site. Every setting is optional.</summary>
public sealed class FeatureSettings
{
    /// <summary>Route prefixes that need a signed-in visitor (the crawler cannot see them without logging in).</summary>
    public string[] SignedInPrefixes { get; init; } = [];

    /// <summary>The curated list of functions (see <see cref="FeatureRegistryFile"/>), relative to the root. Default: quality/features.json; without the file the map shows pages only.</summary>
    public string? Registry { get; init; }

    /// <summary>The page smoke test's result file (a browser test opens every page and writes one outcome per page), relative to the root. Default: reports/browser-data/page-smoke.json; without it pages show "not run".</summary>
    public string? SmokeResults { get; init; }

    /// <summary>The performance notes whose tables classify each service and store (<c>| `IQuizRepository` | Cached | ... |</c>), relative to the root. Default: docs/performance.md.</summary>
    public string? PerformanceDoc { get; init; }

    /// <summary>The folder of markdown docs searched for mentions of each function's pages. Default: docs.</summary>
    public string? DocsFolder { get; init; }

    /// <summary>How many days of git history count as "changed lately" for a function's source. Default 90.</summary>
    public int ChurnDays { get; init; } = 90;
}
