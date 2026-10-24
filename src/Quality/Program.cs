/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

// Code-quality check, run on demand to keep quality from drifting. Four independent checks, run alone or together:
//
//   smells      Roslyn reads our C# and the Razor pages: long or branchy or deeply nested methods, too many parameters, files that grew too big, hidden errors (empty or catch-all
//               catch blocks), blocking on async work. Seconds, no build needed.
//   analyzers   A full build with the .NET code-quality and style analyzers turned up to "Recommended" (CA*/IDE* rules), compared per rule and file. About a minute.
//   scripts     The type check and eslint for the browser scripts (needs Node).
//   crawl       Walks the public site in headless Chromium (Playwright) and reports broken links, script and network errors, SEO basics, sideways scrolling on a phone and axe-core
//               accessibility violations. Read-only; the default target is crawl.defaultUrl in quality/quality.json (usually staging).
//
// Settings that differ per site (solution, folders, limits, script checks, crawl address) live in quality/quality.json in the repository being checked; see ToolConfig.
//
// smells and analyzers are a RATCHET: quality/baseline.json (committed) records what exists today; a run fails only on what is new or worse, so old code is cleaned up at leisure
// and new code cannot add to the pile unnoticed. After fixing things (or deliberately accepting something), run with --update-baseline and commit the file.
//
// Usage (from the root of the repository being checked; "quality" stands for: dotnet run --project <path to src/Quality> --):
//   quality                                                         # the default set: smells, analyzers, scripts
//   quality smells [--include-tests] [--top 25] [--update-baseline]
//   quality analyzers [--mode Recommended|All] [--update-baseline]
//   quality scripts
//   quality crawl [--url https://staging.example.com] [--max-pages 120] [--desktop] [--delay 150]
//   quality setup [--skip <id> --reason "..."] [--acknowledge]          # the first-run checklist: what this site has not done yet, ranked by value for the time
//   quality quick [--since origin/master] [--no-tests] [--no-build]   # small iterations: smells of just the changed files + just the tests that mention them; writes nothing
//   quality all --url http://localhost:5199     # everything, including a crawl of a local site
//   add --open to any run to open the HTML report (reports/quality-latest.html) in Chrome; quality.json's "openReport" ("always" / "onFindings") does it without the flag, --no-open overrides it
// Exit code: 0 clean, 1 something new/worse or a crawl error, 2 a check could not run. A report is written to reports/quality-<date>_<time>.txt (a dated copy per run, so several runs a day all survive).
//
// One-time for the crawler: `pwsh <the tool folder>/bin/Debug/net10.0/playwright.ps1 install chromium`, and `npm install` in the repository (axe-core, the accessibility engine, is a dev dependency there).

using System.Text;
using Microsoft.Playwright;
using CodeQuality.Common;
using CodeQuality.Quality;

internal static class Program
{
    private static readonly string[] Commands = { "default", "all", "smells", "analyzers", "scripts", "crawl", "tests", "history", "features", "quick", "setup", "init", "browser" };
    private static readonly string[] LiveCommands = { "default", "all", "smells", "analyzers", "scripts", "crawl", "tests", "quick" };

    private static async Task<int> Main(string[] args)
    {
        ApplyParkedSwitch(args);

        var command = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "default";
        if (!Commands.Contains(command))
        {
            Console.Error.WriteLine($"Unknown command '{command}'. Use smells, analyzers, scripts, crawl, history or all.");
            return 2;
        }

        var options = new Options(args);
        var root = Repo.FindRoot();

        // The live view covers the commands that run checks; the others (history, features, setup...) are over in a moment.
        using var live = LiveCommands.Contains(command) ? LiveSession.Begin(root, command, options) : LiveRun.Off;
        var finished = false;
        try
        {
            var worst = await RunStandaloneAsync(command, root, options, live) ?? await RunRecordedAsync(command, root, options, live);
            finished = true;
            if (live != LiveRun.Off)
            {
                Console.WriteLine(live.Complete(worst));
            }

            return worst;
        }
        finally
        {
            if (!finished)
            {
                live.Abort("it ended without a result (an error is printed above)");
            }
        }
    }

    /// <summary>The checks that are recorded: runs them, then writes the report, the timeline and the dashboard.</summary>
    private static async Task<int> RunRecordedAsync(string command, string root, Options options, LiveRun live)
    {
        var report = new StringBuilder();
        var config = ToolConfig.Load(root);
        var collected = Collected.Load(root);
        void Say(string line = "")
        {
            Console.WriteLine(line);
            report.AppendLine(line);
        }

        Say($"Quality check, {DateTime.Now:yyyy-MM-dd HH:mm}");
        var context = new RunContext(root, options, Say, collected, config) { Live = live };
        var worst = await RunChecksAsync(command, context);
        await live.RunStageAsync(LiveSteps.Report, "Report, timeline and dashboard", () => WriteReportsAsync(context, worst));

        Say();
        Say(worst switch { 0 => "All clear.", 1 => "Something new or worse needs a look.", _ => "A check could not run." });
        AppendSetupNotice(Say, root, config, collected);
        var file = Path.Combine(Repo.OutputRoot(root), "reports", $"quality-{DateTime.Now:yyyy-MM-dd_HHmm}.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, report.ToString());
        Console.WriteLine($"Report: {file}");
        return worst;
    }

    /// <summary>Saves what the run collected and writes the HTML report, the timeline entry and the dashboard; opens the report when the settings say so. Returns 0: the verdict is the checks', not the report's.</summary>
    private static async Task<int> WriteReportsAsync(RunContext context, int worst)
    {
        var (root, options, say, collected, config) = context;
        TakeLatestResults(root, config, collected);
        collected.Save(root);
        var history = await RecordHistoryAsync(root, collected);
        var html = await HtmlReport.WriteAsync(root, collected, Baseline.Load(root), history, config, options.Flag("--include-tests"));
        say(string.Empty);
        if (history.Summary() is { } trend)
        {
            say(trend);
        }

        say($"HTML report: {html}");
        say($"Dashboard: {await Dashboard.WriteAsync(root, history, config)}");
        if (HtmlReport.ShouldOpen(config.OpenReport, worst, options.Flag("--open"), options.Flag("--no-open")))
        {
            HtmlReport.Open(html, force: options.Flag("--open"));
        }

        return 0;
    }

    // --include-parked: look at the code, tools and tests quality.json has set aside (the same switch as the environment variable, so every child process of this run agrees).
    private static void ApplyParkedSwitch(string[] args)
    {
        if (args.Contains("--include-parked"))
        {
            Environment.SetEnvironmentVariable(ToolConfig.IncludeParkedVariable, "1");
        }
    }

    // Whatever the test projects last wrote (the pre-push check writes it on every push, `quality tests` on demand), so every report shows the latest test numbers, and the browser tests' run log.
    private static void TakeLatestResults(string root, ToolConfig config, Collected collected)
    {
        if (TestResults.Read(root, config) is { } tests)
        {
            collected.Tests = tests;
            collected.TestsAt = tests.At;
        }

        collected.BrowserRuns = BrowserRuns.Read(root, config);
    }

    /// <summary>The commands that are not one of the recorded checks (no report, no timeline entry of their own): history, features, quick and setup. Null for the rest.</summary>
    private static async Task<int?> RunStandaloneAsync(string command, string root, Options options, LiveRun live) => command switch
    {
        "history" => await HistoryCommand(root, options),
        "features" => await FeaturesCommand(root, options),
        "quick" => await QuickCommand.RunAsync(root, options, ToolConfig.Load(root), live),
        "setup" => SetupCommand.Run(root, options, ToolConfig.Load(root)),
        "browser" => BrowserCommand.Run(root, options, ToolConfig.Load(root)),
        "init" => await InitCommand.RunAsync(root, ToolConfig.Load(root), options.Flag("--accept-baselines")),
        _ => null,
    };

    /// <summary>Ends a run with the first-run list (a site never reviewed) or a one-line reminder (items still outstanding): the line Claude reads and passes on. Nothing when the site is set up.</summary>
    private static void AppendSetupNotice(Action<string> say, string root, ToolConfig config, Collected collected)
    {
        if (SetupCommand.Notice(root, config, collected) is { Length: > 0 } notice)
        {
            say(string.Empty);
            say(notice);
        }
    }

    /// <summary>features: writes reports/feature-map.html (every page, clicks from the home page, the tests that mention it, whether the crawler visits it) and opens it unless --no-open.</summary>
    private static async Task<int> FeaturesCommand(string root, Options options)
    {
        if (options.Flag("--generate"))
        {
            return await FeaturesGenerateAsync(root, ToolConfig.Load(root));
        }

        var path = await FeatureMap.WriteAsync(root, ToolConfig.Load(root));
        Console.WriteLine($"Feature map: {path}");
        if (options.Flag("--open"))
        {
            HtmlReport.Open(path, force: true);
        }

        return 0;
    }

    /// <summary>features --generate: drafts quality/features.json from the code (no tokens), then redraws the map so what the draft covers can be read at once.</summary>
    private static async Task<int> FeaturesGenerateAsync(string root, ToolConfig config)
    {
        var (path, proposed, drafted) = await FeatureGenerator.WriteAsync(root, config);
        Console.WriteLine(proposed
            ? $"quality/features.json already exists, so it was not touched. {drafted.Functions.Count} draft(s) for pages it does not cover: {path}"
            : $"Drafted {drafted.Functions.Count} function(s) from the code ({drafted.NotFunctions.Count} page(s) set aside as not a function): {path}");
        Console.WriteLine("Every drafted entry is marked \"generated\": review the names, steps and who may do each, then clear the mark.");
        if (!proposed)
        {
            Console.WriteLine($"Feature map: {await FeatureMap.WriteAsync(root, config)}");
        }

        return 0;
    }

    /// <summary>Adds this run to the quality timeline (quality/history.json) when its numbers changed, and returns the timeline.</summary>
    private static async Task<History> RecordHistoryAsync(string root, Collected collected)
    {
        var history = History.Load(root);
        var commit = (await Shell.RunAsync("git", "rev-parse --short HEAD", root, 20)).Output.Trim();
        if (history.Record(collected, commit, DateTime.Now))
        {
            history.Save(root);
        }

        return history;
    }

    /// <summary>history --backfill [--force]: rebuilds the timeline from the git history of the baseline file (only when none exists, unless --force).</summary>
    private static async Task<int> HistoryCommand(string root, Options options)
    {
        if (!options.Flag("--backfill"))
        {
            var current = History.Load(root);
            Console.WriteLine($"{current.Snapshots.Count} snapshot(s) in quality/history.json. {current.Summary()}");
            return 0;
        }

        if (History.Load(root).Snapshots.Count > 0 && !options.Flag("--force"))
        {
            Console.Error.WriteLine("quality/history.json already has snapshots; --force rebuilds it from git (earlier hand-added notes are lost).");
            return 2;
        }

        var rebuilt = await History.BackfillAsync(root);
        rebuilt.Save(root);
        Console.WriteLine($"Rebuilt {rebuilt.Snapshots.Count} snapshot(s) from git. {rebuilt.Summary()}");
        return 0;
    }

    /// <summary>Runs the checks the command asks for and returns the worst exit code among them.</summary>
    private static async Task<int> RunChecksAsync(string command, RunContext context)
    {
        var (root, options, say, collected, config) = context;
        var worst = 0;
        var all = command == "all";
        var standard = command == "default" || all;
        var smells = standard || command == "smells";
        var analyzers = standard || command == "analyzers";
        var scripts = standard || command == "scripts";
        var tests = all || command == "tests";
        var crawl = all || command == "crawl";
        DeclareSteps(context, smells, analyzers, scripts, tests, crawl);

        if (smells)
        {
            worst = Math.Max(worst, await context.Live.RunStageAsync(LiveSteps.Smells, "Code smells", () => Task.FromResult(SmellsStep(context))));
        }

        if (analyzers)
        {
            worst = Math.Max(worst, await context.Live.RunStageAsync(LiveSteps.Analyzers, ".NET analyzers (a full build)", () => AnalyzersStep(context)));
        }

        if (scripts)
        {
            worst = Math.Max(worst, await context.Live.RunStageAsync(LiveSteps.Scripts, "Browser scripts (type check and eslint)", () => ScriptsStep(context)));
        }

        if (tests)
        {
            say(string.Empty);
            say("== Tests and coverage (every test project, with the coverage collector)");
            worst = Math.Max(worst, await TestRunner.RunAsync(context));
        }

        if (crawl)
        {
            worst = Math.Max(worst, await context.Live.RunStageAsync(LiveSteps.Crawl, "Site crawl", () => CrawlStep(context)));
        }

        return worst;
    }

    // Lists every step up front, in running order, so the live page shows what is still to come and the estimate covers the whole run.
    private static void DeclareSteps(RunContext context, bool smells, bool analyzers, bool scripts, bool tests, bool crawl)
    {
        var live = context.Live;
        var steps = new List<(bool On, string Id, string Label)>
        {
            (smells, LiveSteps.Smells, "Code smells"),
            (analyzers, LiveSteps.Analyzers, ".NET analyzers (a full build)"),
            (scripts, LiveSteps.Scripts, "Browser scripts (type check and eslint)"),
        };
        foreach (var (on, id, label) in steps.Where(s => s.On))
        {
            live.Declare(id, label);
        }

        if (tests)
        {
            foreach (var project in TestRunner.Projects(context.Root, context.Config, TestRunner.IncludeBrowser(context.Options)))
            {
                var name = Path.GetFileNameWithoutExtension(project);
                live.Declare(LiveSteps.Tests(name), $"Tests: {name}");
            }
        }

        if (crawl)
        {
            live.Declare(LiveSteps.Crawl, "Site crawl");
        }

        live.Declare(LiveSteps.Report, "Report, timeline and dashboard");
    }

    private static async Task<int> ScriptsStep(RunContext context)
    {
        var (root, _, say, collected, config) = context;
        say(string.Empty);
        say("== Browser scripts (type check + eslint)");
        var (ok, summary) = await Scripts.RunAsync(root, config);
        say(summary);
        collected.Scripts = summary;
        collected.ScriptsAt = Collected.Now;
        return ok ? 0 : 1;
    }

    private static int SmellsStep(RunContext context)
    {
        var (root, options, say, collected, config) = context;
        say(string.Empty);
        say("== Code smells (Roslyn over our C#, plus Razor page sizes)");
        var findings = Smells.Scan(root, options.Flag("--include-tests"), config);
        collected.Smells = findings;
        collected.SmellsAt = Collected.Now;
        collected.RanThisRun.Add("smells");
        return Ratchet(context, findings, key => !key.StartsWith("build/", StringComparison.Ordinal), "smells", LiveSteps.Smells);
    }

    private static async Task<int> AnalyzersStep(RunContext context)
    {
        var (root, options, say, collected, config) = context;
        say(string.Empty);
        say($"== .NET analyzers ({options.Text("--mode", "Recommended")} mode; a full build, so this takes a minute)");
        var (findings, problem) = await Analyzers.RunAsync(root, options.Text("--mode", "Recommended"), config);
        if (problem is not null)
        {
            say(problem);
            return 2;
        }

        collected.Analyzers = findings;
        collected.AnalyzersAt = Collected.Now;
        collected.RanThisRun.Add("analyzers");
        return Ratchet(context, findings, key => key.StartsWith("build/", StringComparison.Ordinal), "analyzers", LiveSteps.Analyzers);
    }

    /// <summary>Compares findings with the baseline: prints a summary of everything, and the detail of what is new or worse. Returns the exit code.</summary>
    private static int Ratchet(RunContext context, IReadOnlyList<Finding> findings, Func<string, bool> ownKeys, string what, string stage)
    {
        var (root, options, say, _, _) = context;
        var top = options.Int("--top", 20);
        var byRule = findings.GroupBy(f => f.Rule).OrderByDescending(g => g.Count());
        say($"{findings.Count} finding(s): " + string.Join(", ", byRule.Select(g => $"{g.Key} {g.Count()}")));

        var baseline = Baseline.Load(root);
        if (options.Flag("--update-baseline"))
        {
            var kept = baseline.Entries.Where(e => !ownKeys(e.Key)).ToDictionary(e => e.Key, e => e.Value);
            foreach (var (key, value) in Baseline.From(findings).Entries)
            {
                kept[key] = value;
            }

            new Baseline { Entries = kept }.Save(root);
            say($"Baseline updated for {what}: {findings.Count} finding(s) accepted. Commit quality/baseline.json.");
            return 0;
        }

        if (!baseline.Entries.Keys.Any(ownKeys))
        {
            say($"No baseline for {what} yet, so nothing can be called new. Look at the worst offenders below, then accept today's state with --update-baseline.");
            foreach (var f in findings.OrderByDescending(f => f.Value).ThenBy(f => f.File).Take(top))
            {
                say($"  {f.Rule,-14} {f.File}:{f.Line}  {f.Message}");
            }

            return 0;
        }

        var comparison = baseline.Compare(findings, ownKeys);
        say($"Against the baseline: {comparison.New.Count} new, {comparison.Worse.Count} worse, {comparison.Fixed.Count} fixed since it was recorded.");
        context.Live.AddFindings(stage, comparison.New, comparison.Worse);
        context.Live.Note(stage, $"{findings.Count} finding(s); {comparison.New.Count} new, {comparison.Worse.Count} worse, {comparison.Fixed.Count} fixed");
        foreach (var f in comparison.New.Take(top))
        {
            say($"  NEW   {f.Rule,-14} {f.File}:{f.Line}  {f.Message}");
        }

        foreach (var (f, was) in comparison.Worse.Take(top))
        {
            say($"  WORSE {f.Rule,-14} {f.File}:{f.Line}  was {was}, now {f.Value}: {f.Message}");
        }

        if (comparison.Fixed.Count > 0 && comparison.Clean)
        {
            say($"  Some old findings are gone - run with --update-baseline to lock in the improvement.");
        }

        return comparison.Clean ? 0 : 1;
    }

    private static async Task<int> CrawlStep(RunContext context)
    {
        var (root, options, say, collected, config) = context;
        var url = options.Text("--url", config.Crawl.DefaultUrl ?? string.Empty);
        if (string.IsNullOrWhiteSpace(url))
        {
            say("No address to crawl: pass --url, or set crawl.defaultUrl in quality/quality.json.");
            return 2;
        }

        say(string.Empty);
        say($"== Site crawl of {url} ({(options.Flag("--desktop") ? "desktop" : "phone")}-sized, read-only)");
        CrawlResult result;
        try
        {
            result = await Crawler.RunAsync(root, url, options.Int("--max-pages", 120), options.Flag("--desktop"), options.Int("--delay", 150), config.Crawl);
        }
        catch (Exception ex) when (ex is PlaywrightException or HttpRequestException or InvalidOperationException)
        {
            say("The crawl could not run: " + ex.Message);
            say($"If the browser is missing: pwsh {Path.Combine(AppContext.BaseDirectory, "playwright.ps1")} install chromium");
            return 2;
        }

        collected.Crawl = result;
        collected.CrawlUrl = url;
        collected.CrawlAt = Collected.Now;
        var errors = result.Issues.Count(i => i.Severity == Severity.Error);
        var warnings = result.Issues.Count - errors;
        say($"{result.Pages} page(s) visited: {errors} error(s), {warnings} warning(s).");
        foreach (var severity in result.Issues.GroupBy(i => i.Severity).OrderBy(g => g.Key))
        {
            say($"-- {severity.Key}s");
            // The same problem on many pages is one line: "No meta description. (45 pages: /demo, /demo/abc, ...)".
            foreach (var same in severity.GroupBy(i => i.Text).OrderByDescending(g => g.Count()).Take(options.Int("--top", 60)))
            {
                var pages = same.Select(i => i.Page).Distinct().ToList();
                say(pages.Count == 1 ? $"  {pages[0]}  {same.Key}" : $"  {same.Key}  ({pages.Count} pages: {string.Join(", ", pages.Take(3))}{(pages.Count > 3 ? ", ..." : string.Empty)})");
            }
        }

        return errors > 0 ? 1 : 0;
    }
}

internal sealed class Options
{
    private readonly string[] _args;

    public Options(string[] args) => _args = args;

    public bool Flag(string name) => _args.Contains(name);

    public string Text(string name, string fallback)
    {
        var i = Array.IndexOf(_args, name);
        return i >= 0 && i + 1 < _args.Length ? _args[i + 1] : fallback;
    }

    public int Int(string name, int fallback) => int.TryParse(Text(name, string.Empty), out var v) ? v : fallback;
}

/// <summary>Everything one run of the checks works with, so each step takes one argument instead of five.</summary>
internal sealed record RunContext(string Root, Options Options, Action<string> Say, Collected Collected, ToolConfig Config)
{
    /// <summary>The live view of this run; a property so the many places that take a context apart by position are untouched.</summary>
    public LiveRun Live { get; init; } = LiveRun.Off;
}
