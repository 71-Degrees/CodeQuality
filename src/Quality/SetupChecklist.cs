/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;

namespace CodeQuality.Quality;

internal enum SetupState
{
    Done,
    Missing,

    /// <summary>The site has nothing for this check to look at (no npm project, no deploy targets).</summary>
    NotApplicable,

    /// <summary>The owner decided not to, and said why (<c>quality setup --skip</c>).</summary>
    Skipped,
}

/// <summary>
/// One thing a site does once, when it first gets Code Quality. <see cref="Benefit"/> is 1-5 (what it tells you or prevents), <see cref="RunMinutes"/> the machine time to run it,
/// <see cref="EffortMinutes"/> the person-or-Claude time to set it up, <see cref="NeedsApproval"/> true when running it touches something outside the repository (real traffic to a site)
/// so it waits for the owner's OK.
/// </summary>
internal sealed record SetupItem(string Id, string Title, int Benefit, int RunMinutes, int EffortMinutes, bool NeedsApproval, string How);

internal sealed record SetupStatus(SetupItem Item, SetupState State, string Note);

/// <summary><c>quality/setup.json</c> (committed): that the first run was reviewed, and the deliberate "no"s with their reasons, so the notice stops for what was decided.</summary>
internal sealed class SetupFile
{
    public string? FirstRun { get; set; }

    public Dictionary<string, SkipNote> Skipped { get; set; } = new();

    public static string PathFor(string root) => Path.Combine(root, "quality", "setup.json");

    public static bool Exists(string root) => File.Exists(PathFor(root));

    public static SetupFile Load(string root) => JsonState.Load<SetupFile>(PathFor(root));

    public void Save(string root) => JsonState.Save(PathFor(root), this);
}

internal sealed record SkipNote(string Reason, string At);

/// <summary>What the checklist looks at, so each detection is a plain function of the repository's state.</summary>
internal sealed record SetupFacts(string Root, ToolConfig Config, Collected Collected, TestSummary? Tests, SetupFile File);

/// <summary>
/// The first-run checklist: every once-per-site thing Code Quality can do (the baselines, the push-gate wiring, coverage, the feature map, the crawl, the scripts check, browser tests, the release panel,
/// push timings), each worked out from the repository's own state - nothing to remember to tick - and ranked: free value first, anything that needs the owner's OK last.
/// </summary>
internal static class SetupChecklist
{
    private static string Tool(ToolConfig config) => config.ToolCommand.TrimEnd();

    internal static IReadOnlyList<SetupStatus> Evaluate(SetupFacts facts)
    {
        var baseline = Baseline.Load(facts.Root).Entries.Keys.ToList();
        var statuses = new List<SetupStatus>
        {
            Of(Item("baseline", "Accept the smells baseline", 5, 1, 2, false, $"{Tool(facts.Config)} smells --update-baseline   (read the findings first; accepting them is the owner's call)"), baseline.Any(k => !k.StartsWith("build/", StringComparison.Ordinal)), "quality/baseline.json has no smell entries yet"),
            Of(Item("analyzer-baseline", "Accept the .NET analyzer baseline", 4, 2, 2, false, $"{Tool(facts.Config)} analyzers --update-baseline"), baseline.Any(k => k.StartsWith("build/", StringComparison.Ordinal)) || facts.Collected.Analyzers.Count > 0, "the analyzers have never been run"),
            Of(Item("gate", "Run the smells ratchet in the push gate", 5, 0, 15, false, "add the smells step to the repository's pre-push check (it fails on NEW or WORSE findings only); see tools/Deploy/QualityChecks.cs in MyWave"), GateWired(facts.Root), "nothing in tools/Deploy runs CodeQuality, so new smells reach the remote"),
            Of(Item("reports-ignored", "Keep reports/ out of git", 1, 0, 1, false, "add `reports/` to .gitignore"), GitIgnoresReports(facts.Root), ".gitignore does not list reports/"),
            Of(Item("tests-coverage", "Measure the tests and their coverage", 4, 3, 1, false, $"{Tool(facts.Config)} tests"), facts.Tests is { Coverage.Count: > 0 }, "no coverage results under reports/test-results"),
            Of(Item("features", "Draw the feature map and list the site's functions", 4, 1, 40, false, $"{Tool(facts.Config)} features --no-open, then write quality/features.json (what a person does, who may, which pages, which tests)"), File.Exists(Path.Combine(facts.Root, facts.Config.Features?.Registry ?? "quality/features.json")), "no quality/features.json, so the dashboard's feature-coverage chart has nothing to draw"),
            Of(Item("analyzers-on", "Switch the analyzers on in Directory.Build.props", 3, 1, 10, false, "set AnalysisMode=Recommended (and CodeAnalysisTreatWarningsAsErrors once the baseline exists) in Directory.Build.props"), AnalyzersOn(facts.Root), "the analyzers only run when asked, not in every build"),
            Scripts(facts),
            Crawl(facts),
            Of(Item("browser-tests", "Add browser tests on the shared fixture", 4, 2, 90, false, "a test project referencing src/Quality.Browser (BrowserSiteFixture, [BrowserFact]); keep runs small - each browser is about 100 MB"), BrowserTests(facts), "no browser tests, so nothing walks the site the way a person does"),
            ReleasePanel(facts),
            Of(Item("push-timings", "Record how long each stage of the push check takes", 2, 0, 20, false, "have the push check append to reports/check-timings.jsonl (the shared WebCore deploy engine does it by itself)"), File.Exists(Path.Combine(Repo.OutputRoot(facts.Root), "reports", "check-timings.jsonl")), "reports/check-timings.jsonl does not exist"),
        };

        return statuses.Select(s => Decided(s, facts.File)).ToList();
    }

    /// <summary>Best first: what is not waiting for approval, then value for the time it takes.</summary>
    internal static IReadOnlyList<SetupStatus> Outstanding(IEnumerable<SetupStatus> statuses) =>
        statuses.Where(s => s.State == SetupState.Missing)
            .OrderBy(s => s.Item.NeedsApproval)
            .ThenByDescending(s => s.Item.Benefit * 60.0 / (s.Item.RunMinutes + s.Item.EffortMinutes + 2))
            .ToList();

    private static SetupItem Item(string id, string title, int benefit, int run, int effort, bool approval, string how) => new(id, title, benefit, run, effort, approval, how);

    private static SetupStatus Of(SetupItem item, bool done, string missingNote) =>
        new(item, done ? SetupState.Done : SetupState.Missing, done ? string.Empty : missingNote);

    private static SetupStatus Decided(SetupStatus status, SetupFile file) =>
        status.State == SetupState.Missing && file.Skipped.TryGetValue(status.Item.Id, out var skip)
            ? status with { State = SetupState.Skipped, Note = $"skipped {skip.At}: {skip.Reason}" }
            : status;

    private static SetupStatus Scripts(SetupFacts facts)
    {
        var item = Item("scripts", "Type-check and lint the browser scripts", 2, 1, 20, false, $"configure scriptChecks in quality/quality.json, then {Tool(facts.Config)} scripts");
        if (!File.Exists(Path.Combine(facts.Root, "package.json")))
        {
            return new SetupStatus(item, SetupState.NotApplicable, "no package.json: no npm project to check");
        }

        return Of(item, facts.Config.ScriptChecks.Length > 0 && facts.Collected.Scripts is not null, "scriptChecks is empty or has never run");
    }

    private static SetupStatus Crawl(SetupFacts facts)
    {
        var item = Item("crawl", "Crawl the staging site (links, console errors, SEO, accessibility)", 3, 4, 10, true, $"set crawl.defaultUrl, install the browser once (pwsh <tool folder>/bin/Debug/net10.0/playwright.ps1 install chromium), then {Tool(facts.Config)} crawl --max-pages 20 - real traffic to the site, so it waits for the owner's OK");
        var url = facts.Config.Crawl.DefaultUrl;
        return Of(item, facts.Collected.Crawl is not null, string.IsNullOrWhiteSpace(url) ? "no crawl.defaultUrl in quality/quality.json yet" : $"never run against {url}");
    }

    private static SetupStatus ReleasePanel(SetupFacts facts)
    {
        var item = Item("release-panel", "Show what staging and production run on the dashboard", 2, 0, 20, false, "add a `release` block to quality/quality.json (version URLs per target and the deploy console address)");
        var hasTargets = File.Exists(Path.Combine(facts.Root, "tools", "Deploy", "targets.json")) || File.Exists(Path.Combine(facts.Root, "deploy", "deploy.json"));
        return hasTargets
            ? Of(item, facts.Config.Release is not null, "the repository deploys somewhere but the dashboard does not show where")
            : new SetupStatus(item, SetupState.NotApplicable, "no deploy targets in this repository");
    }

    /// <summary>True when something in tools/Deploy mentions CodeQuality or the shared WebCore deploy engine (which runs the smells step itself).</summary>
    private static bool GateWired(string root)
    {
        var deploy = Path.Combine(root, "tools", "Deploy");
        return Directory.Exists(deploy) && Directory.EnumerateFiles(deploy, "*.*", SearchOption.TopDirectoryOnly)
            .Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .Any(p => File.ReadAllText(p).Contains("CodeQuality", StringComparison.Ordinal) || File.ReadAllText(p).Contains("WebCore.Deploy", StringComparison.Ordinal));
    }

    private static bool GitIgnoresReports(string root) =>
        File.Exists(Path.Combine(root, ".gitignore")) && File.ReadAllLines(Path.Combine(root, ".gitignore")).Any(l => l.Trim().TrimStart('/') is "reports/" or "reports");

    private static bool AnalyzersOn(string root) =>
        File.Exists(Path.Combine(root, "Directory.Build.props")) && File.ReadAllText(Path.Combine(root, "Directory.Build.props")).Contains("AnalysisMode", StringComparison.Ordinal);

    private static bool BrowserTests(SetupFacts facts) =>
        !string.IsNullOrWhiteSpace(facts.Config.BrowserRunLog)
        || facts.Config.TestFolders.Select(f => Path.Combine(facts.Root, f)).Where(Directory.Exists)
            .SelectMany(f => Directory.EnumerateFiles(f, "*.csproj", SearchOption.AllDirectories))
            .Any(p => File.ReadAllText(p).Contains("Quality.Browser", StringComparison.Ordinal));
}
