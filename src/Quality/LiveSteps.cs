/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Quality;

/// <summary>The names of a run's steps. The ones a full run shares with the push check ("code smells", "scripts: type check and lint", "tests: ...") are spelled the same, so <c>reports/check-timings.jsonl</c> gives their first estimates.</summary>
internal static class LiveSteps
{
    internal const string Smells = "code smells";
    internal const string Analyzers = "analyzers";
    internal const string Scripts = "scripts: type check and lint";
    internal const string Crawl = "site crawl";
    internal const string Report = "report";
    internal const string QuickSmells = "quick: code smells";
    internal const string QuickScripts = "quick: scripts";

    internal static string Tests(string project) => "tests: " + project;

    internal static string QuickTests(string project) => "quick tests: " + project;
}
