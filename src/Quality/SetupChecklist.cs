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

/// <summary>How much of something a step uses: nothing, a little, a fair amount, a lot.</summary>
internal enum Level
{
    None,
    Low,
    Medium,
    High,
}

/// <summary>
/// What a step costs. <see cref="RunMinutes"/> is machine time to run it and <see cref="EffortMinutes"/> the time to set it up (Claude's or the owner's); <see cref="NeedsApproval"/> is true when running it
/// touches something outside the repository (real traffic to a site), so it waits for the owner's OK. <see cref="Engine"/> says what executes the bulk of the work - a .NET tool, Node, a browser, or Claude editing
/// files - <see cref="Tokens"/> what Claude's own reading, writing and thinking costs in tokens for a site of the given size (the part that burns the owner's session; a .NET run costs none), and
/// <see cref="Machine"/> how hard it is on the computer, with its reason.
/// </summary>
internal sealed record SetupCost(int RunMinutes, int EffortMinutes, bool NeedsApproval, string Engine, Func<SiteSize, TokenRange> Tokens, Level Machine, string MachineWhy);

/// <summary>One thing a site does once, when it first gets Code Quality. <see cref="Benefit"/> is 1-5 (what it tells you or prevents); <see cref="How"/> may contain {tool}, replaced by the site's tool command.</summary>
internal sealed record SetupItem(string Id, string Title, int Benefit, string What, SetupCost Cost, string How)
{
    public bool NeedsApproval => Cost.NeedsApproval;

    public int RunMinutes => Cost.RunMinutes;

    public int EffortMinutes => Cost.EffortMinutes;
}

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
/// The first-run checklist: every once-per-site thing Code Quality can do, each worked out from the repository's own state - nothing to remember to tick - and ranked: free value first, anything that needs the
/// owner's OK last. The catalogue is data (what each step does and costs); <see cref="Evaluate"/> decides which are done.
/// </summary>
internal static class SetupChecklist
{
    internal static readonly IReadOnlyList<SetupItem> Catalog =
    [
        new("baseline", "Accept the smells baseline", 5,
            "Runs the smell scan over every C# and Razor file and records today's findings as the accepted starting point (quality/baseline.json); from then on only NEW or WORSE findings fail.",
            new(1, 2, false, ".NET tool (CodeQuality; no build)", Fixed(3_000, 8_000, "Claude reads the findings summary and asks before accepting them"), Level.Low, "a Roslyn read of the source: seconds, no build"),
            "{tool} smells --update-baseline   (read the findings first; accepting them is the owner's call)"),
        new("analyzer-baseline", "Accept the .NET analyzer baseline", 4,
            "Builds the whole solution into a scratch folder with the .NET analyzers at Recommended and records the warnings as accepted, the same way.",
            new(2, 2, false, ".NET tool driving `dotnet build`", Fixed(2_000, 5_000, "Claude reads a per-rule summary"), Level.Medium, "a full build: CPU and disk for about a minute"),
            "{tool} analyzers --update-baseline"),
        new("gate", "Run the smells ratchet in the push gate", 5,
            "Adds a smells step to the repository's pre-push check, so a push with new or worse findings is refused instead of discovered later.",
            new(0, 15, false, "Claude edits C# in tools/Deploy; the check itself is the .NET tool", Fixed(12_000, 25_000, "Claude reads the existing pattern, writes a small class and proves it with a test and a push"), Level.Low, "one build and one test run to prove it"),
            "add the smells step to the repository's pre-push check (it fails on NEW or WORSE findings only); see tools/Deploy/QualityChecks.cs in MyWave"),
        new("reports-ignored", "Keep reports/ out of git", 1,
            "Adds `reports/` to .gitignore so the generated reports never get committed.",
            new(0, 1, false, "Claude edits one file", Fixed(500, 1_500, "a one-line edit"), Level.None, "nothing runs"),
            "add `reports/` to .gitignore"),
        new("tests-coverage", "Measure the tests and their coverage", 4,
            "Runs every test project with the coverage collector and records pass/fail counts and line coverage per project folder, for the report and the trend.",
            new(3, 1, false, ".NET (`dotnet test`, started by the CodeQuality tool)", Fixed(2_000, 4_000, "Claude reads a one-screen summary"), Level.Medium, "builds and runs every test: CPU for about as long as the repository's full test suite"),
            "{tool} tests"),
        new("features", "Draw the feature map and list the site's functions", 4,
            "Draws a map of every page (clicks from the home page, which tests mention it, whether the crawler reaches it) and drafts quality/features.json from the code: the site's functions, who may do each, the steps, the pages and the tests. Drafted entries are marked generated until someone reviews them.",
            new(1, 15, false, "the .NET tool drafts it (`features --generate`); Claude or you only review the draft", FeaturesTokens, Level.Low, "the map and the draft are seconds, no build"),
            "{tool} features --generate (writes quality/features.json, or quality/features-proposed.json beside an existing list), then review the names, steps and who may do each and clear the generated mark"),
        new("analyzers-on", "Switch the analyzers on in Directory.Build.props", 3,
            "Turns the .NET analyzers on in every build (AnalysisMode=Recommended), so warnings show as you code instead of only when asked.",
            new(1, 10, false, "Claude edits Directory.Build.props; `dotnet build` shows the effect", Fixed(6_000, 20_000, "Claude edits it, then works through the warnings the build now shows"), Level.Medium, "one full build"),
            "set AnalysisMode=Recommended (and CodeAnalysisTreatWarningsAsErrors once the baseline exists) in Directory.Build.props"),
        new("scripts", "Type-check and lint the browser scripts", 2,
            "Type-checks and lints the site's JavaScript/TypeScript with its npm scripts.",
            new(1, 20, false, "Node (npm run typecheck / lint), started by the .NET tool", Fixed(3_000, 8_000, "Claude reads pass/fail output and fixes what it finds"), Level.Low, "seconds"),
            "configure scriptChecks in quality/quality.json, then {tool} scripts"),
        new("crawl", "Crawl the staging site (links, console errors, SEO, accessibility)", 3,
            "Walks the site's public pages in headless Chromium and reports broken links, console and network errors, SEO basics, sideways scrolling on a phone and accessibility problems (axe-core).",
            new(4, 10, true, "Chromium (Playwright), driven by the .NET tool", Fixed(3_000, 10_000, "Claude reads the issue summary (more if there are many findings)"), Level.Medium, "a headless browser (about 100-200 MB of memory; a one-time download of about 150 MB) and real traffic to the site"),
            "set crawl.defaultUrl, install the browser once (pwsh <tool folder>/bin/Debug/net10.0/playwright.ps1 install chromium), then {tool} crawl --max-pages 20"),
        new("browser-tests", "Add browser tests on the shared fixture", 4,
            "Adds a small project of end-to-end tests that drive a real browser through the site's main journeys, with timings tracked per scenario.",
            new(2, 90, false, "Claude writes the C# tests; each run is Chromium (Playwright) under xUnit", BrowserTestsTokens, Level.High, "each browser is about 100 MB: run a few at a time, never in parallel, and watch free memory"),
            "a test project referencing src/Quality.Browser (BrowserSiteFixture, [BrowserFact]); keep runs small"),
        new("release-panel", "Show what staging and production run on the dashboard", 2,
            "Shows on the dashboard which build each environment runs and how far behind production is, with a button into the deploy console.",
            new(0, 20, false, "Claude edits quality.json; the dashboard asks each version URL when it is drawn", Fixed(2_000, 4_000, "a small config edit"), Level.None, "one web request per environment when the dashboard is built"),
            "add a `release` block to quality/quality.json (version URLs per target and the deploy console address)"),
        new("push-timings", "Record how long each stage of the push check takes", 2,
            "Records how long each stage of the push check takes, so the dashboard can show where the wait goes.",
            new(0, 20, false, "Claude edits tools/Deploy; the recording happens on each push", Fixed(6_000, 15_000, "Claude edits the deploy tool and proves it with a push"), Level.None, "a few milliseconds per push"),
            "have the push check append to reports/check-timings.jsonl (the shared WebCore deploy engine does it by itself)"),
    ];

    private static Func<SiteSize, TokenRange> Fixed(int low, int high, string why) => _ => TokenRange.Fixed(low, high, why);

    /// <summary>The draft costs nothing; what costs is reading it (about 150-400 tokens a page) and fixing the names and steps, plus 2-4k of overhead. Writing the list by hand instead would cost 35-80% more of the markup and tests read in.</summary>
    private static TokenRange FeaturesTokens(SiteSize site) =>
        TokenRange.Fixed(site.Pages * 150 + 2_000, site.Pages * 400 + 4_000,
            $"the draft costs none; Claude only reads and corrects it ({site.Pages} pages). Writing it by hand would read ~{site.RazorTokens / 1000}k tokens of markup and ~{site.TestTokens / 1000}k of tests first");

    /// <summary>About one journey per three pages, each 8-18k tokens to write, run and debug.</summary>
    private static TokenRange BrowserTestsTokens(SiteSize site)
    {
        var journeys = Math.Max(3, site.Pages / 3);
        return TokenRange.Fixed(journeys * 8_000, journeys * 18_000, $"Claude writes and debugs about {journeys} journeys (one per three of this site's {site.Pages} pages): the longest job on this list");
    }

    internal static IReadOnlyList<SetupStatus> Evaluate(SetupFacts facts) => Catalog.Select(item => Decided(Check(item, facts), facts.File)).ToList();

    /// <summary>Best first: what is not waiting for approval, then value for the time it takes.</summary>
    internal static IReadOnlyList<SetupStatus> Outstanding(IEnumerable<SetupStatus> statuses) =>
        statuses.Where(s => s.State == SetupState.Missing)
            .OrderBy(s => s.Item.NeedsApproval)
            .ThenByDescending(s => s.Item.Benefit * 60.0 / (s.Item.RunMinutes + s.Item.EffortMinutes + 2))
            .ToList();

    private static SetupStatus Check(SetupItem item, SetupFacts facts)
    {
        var baseline = Baseline.Load(facts.Root).Entries.Keys.ToList();
        return item.Id switch
        {
            "baseline" => Of(item, baseline.Any(k => !k.StartsWith("build/", StringComparison.Ordinal)), "quality/baseline.json has no smell entries yet"),
            "analyzer-baseline" => Of(item, baseline.Any(k => k.StartsWith("build/", StringComparison.Ordinal)) || facts.Collected.Analyzers.Count > 0, "the analyzers have never been run"),
            "gate" => Of(item, GateWired(facts.Root), "nothing in tools/Deploy runs CodeQuality, so new smells reach the remote"),
            "reports-ignored" => Of(item, GitIgnoresReports(facts.Root), ".gitignore does not list reports/"),
            "tests-coverage" => Of(item, facts.Tests is { Coverage.Count: > 0 }, "no coverage results under reports/test-results"),
            "features" => Of(item, File.Exists(Path.Combine(facts.Root, facts.Config.Features?.Registry ?? "quality/features.json")), "no quality/features.json, so the dashboard's feature-coverage chart has nothing to draw"),
            _ => CheckSite(item, facts),
        };
    }

    /// <summary>The checks about how the site is set up (build props, scripts, crawl, browser tests, deploy), apart from the quality tool's own records.</summary>
    private static SetupStatus CheckSite(SetupItem item, SetupFacts facts)
    {
        return item.Id switch
        {
            "analyzers-on" => Of(item, AnalyzersOn(facts.Root), "the analyzers only run when asked, not in every build"),
            "scripts" => Scripts(item, facts),
            "crawl" => Of(item, facts.Collected.Crawl is not null, string.IsNullOrWhiteSpace(facts.Config.Crawl.DefaultUrl) ? "no crawl.defaultUrl in quality/quality.json yet" : $"never run against {facts.Config.Crawl.DefaultUrl}"),
            "browser-tests" => Of(item, BrowserTests(facts), "no browser tests, so nothing walks the site the way a person does"),
            "release-panel" => ReleasePanel(item, facts),
            "push-timings" => Of(item, File.Exists(Path.Combine(Repo.OutputRoot(facts.Root), "reports", "check-timings.jsonl")), "reports/check-timings.jsonl does not exist"),
            _ => new SetupStatus(item, SetupState.NotApplicable, "unknown item"),
        };
    }

    private static SetupStatus Of(SetupItem item, bool done, string missingNote) =>
        new(item, done ? SetupState.Done : SetupState.Missing, done ? string.Empty : missingNote);

    private static SetupStatus Decided(SetupStatus status, SetupFile file) =>
        status.State == SetupState.Missing && file.Skipped.TryGetValue(status.Item.Id, out var skip)
            ? status with { State = SetupState.Skipped, Note = $"skipped {skip.At}: {skip.Reason}" }
            : status;

    private static SetupStatus Scripts(SetupItem item, SetupFacts facts) =>
        !File.Exists(Path.Combine(facts.Root, "package.json"))
            ? new SetupStatus(item, SetupState.NotApplicable, "no package.json: no npm project to check")
            : Of(item, facts.Config.ScriptChecks.Length > 0 && facts.Collected.Scripts is not null, "scriptChecks is empty or has never run");

    private static SetupStatus ReleasePanel(SetupItem item, SetupFacts facts)
    {
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
            .Select(File.ReadAllText)
            .Any(text => text.Contains("CodeQuality", StringComparison.Ordinal) || text.Contains("WebCore.Deploy", StringComparison.Ordinal));
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
