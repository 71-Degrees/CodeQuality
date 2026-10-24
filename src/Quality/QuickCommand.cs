/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Diagnostics;
using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>
/// <c>quality quick</c>: the fast check for small iterations. It looks only at what the working tree changes (git: staged, unstaged and new files, or <c>--since &lt;ref&gt;</c>) -
/// the smells of just those files against the baseline, then just the tests that mention the types in them - and leaves the full set (every check, every test) for the push.
/// It writes nothing: no report, no timeline entry, no baseline change, so a partial look never stands in for a full one.
/// </summary>
internal static class QuickCommand
{
    /// <summary>The same name Quality.Browser's BrowserRunLog.QuickRunVariable looks for (the two projects do not reference each other).</summary>
    private const string QuickRunVariable = "QUALITY_QUICK";

    internal static async Task<int> RunAsync(string root, Options options, ToolConfig config, LiveRun live)
    {
        var clock = Stopwatch.StartNew();

        // Everything a quick run starts (test runs, the site, the browser) inherits this: none of it may add to the records that are read over time (the browser run log), and none of it publishes anything.
        Environment.SetEnvironmentVariable(QuickRunVariable, "1");
        var since = options.Text("--since", string.Empty);
        var commit = options.Text("--commit", string.Empty);
        var files = commit.Length > 0 ? await ChangedFiles.OfCommitAsync(root, commit) : await ChangedFiles.FindAsync(root, since.Length == 0 ? null : since);
        var changed = ChangedFiles.Sources(files, config);
        var scripts = ChangedFiles.BrowserScripts(files);
        Console.WriteLine($"Quick check of what changed since {(since.Length == 0 ? "HEAD" : since)}: {changed.Count} C#/Razor file(s), {scripts.Count} browser script(s).");
        if (changed.Count == 0 && scripts.Count == 0)
        {
            Console.WriteLine("Nothing to check. (The full check - `quality` and the test suite - is for the push.)");
            return 0;
        }

        foreach (var file in changed)
        {
            Console.WriteLine($"  {(file.IsTest ? "test  " : "source")} {file.Relative}");
        }

        // A changed browser script is checked the way the push checks all of them (type check and eslint run over the whole script folder, a few seconds), so a script-only change is no longer "nothing to check".
        var worst = scripts.Count > 0 ? await live.RunStageAsync(LiveSteps.QuickScripts, "Browser scripts (changed)", () => ScriptsOfChanged(root, config, scripts)) : 0;

        // --no-smells: the push check already ran the whole-repository smells ratchet, so only the tests are wanted here.
        worst = Math.Max(worst, options.Flag("--no-smells") ? 0 : await live.RunStageAsync(LiveSteps.QuickSmells, "Code smells in the changed files", () => Task.FromResult(SmellsOfChanged(root, options, config, changed, live))));
        if (!options.Flag("--no-tests") || options.Flag("--browser"))
        {
            worst = Math.Max(worst, await TestsOfChanged(root, options, config, changed, live));
        }

        Console.WriteLine();
        Console.WriteLine($"{(worst == 0 ? "Quick check clean" : worst == 1 ? "Something new, worse or failing needs a look" : "A check could not run")} ({clock.Elapsed.TotalSeconds:0.#} s). The full check and the whole test suite are still the gate before a push.");
        return worst;
    }

    private static async Task<int> ScriptsOfChanged(string root, ToolConfig config, IReadOnlyList<string> scripts)
    {
        Console.WriteLine();
        Console.WriteLine("== Browser scripts (type check + eslint) - changed:");
        foreach (var script in scripts)
        {
            Console.WriteLine($"  {script}");
        }

        var (ok, summary) = await Scripts.RunAsync(root, config);
        Console.WriteLine(summary);
        return ok ? 0 : 1;
    }

    private static int SmellsOfChanged(string root, Options options, ToolConfig config, IReadOnlyList<ChangedSource> changed, LiveRun live)
    {
        Console.WriteLine();
        Console.WriteLine("== Code smells in the changed files");
        var paths = changed.Select(c => c.Relative).ToHashSet(StringComparer.Ordinal);
        var findings = Smells.Scan(root, options.Flag("--include-tests"), config).Where(f => paths.Contains(f.File)).ToList();
        var baseline = Baseline.Load(root);
        var comparison = baseline.Compare(findings, key => !key.StartsWith("build/", StringComparison.Ordinal) && paths.Contains(FileOf(key)));
        Console.WriteLine($"{findings.Count} finding(s) in them; against the baseline: {comparison.New.Count} new, {comparison.Worse.Count} worse, {comparison.Fixed.Count} fixed.");
        live.AddFindings(LiveSteps.QuickSmells, comparison.New, comparison.Worse);
        foreach (var f in comparison.New)
        {
            Console.WriteLine($"  NEW   {f.Rule,-14} {f.File}:{f.Line}  {f.Message}");
        }

        foreach (var (f, was) in comparison.Worse)
        {
            Console.WriteLine($"  WORSE {f.Rule,-14} {f.File}:{f.Line}  was {was}, now {f.Value}: {f.Message}");
        }

        if (comparison.Fixed.Count > 0 && comparison.Clean)
        {
            Console.WriteLine("  Some old findings in these files are gone - run `quality smells --update-baseline` to lock in the improvement.");
        }

        return comparison.Clean ? 0 : 1;
    }

    private static async Task<int> TestsOfChanged(string root, Options options, ToolConfig config, IReadOnlyList<ChangedSource> changed, LiveRun live)
    {
        Console.WriteLine();
        Console.WriteLine("== Tests that mention the changed code");
        var projects = TestRunner.Projects(root, config, includeBrowser: options.Flag("--browser"));   // --browser: the site's own browser test classes the selected functions name run too
        var plan = WithoutGeneratedJourneys(root, config, TestSelection.Plan(root, changed, projects, config.SourceFolders, FeatureRegistryFile.Load(root, config.Features)));
        foreach (var function in plan.Functions)
        {
            Console.WriteLine($"  function: {function.Name} ({(function.Tests.Length == 0 ? "no test is named for it" : string.Join(", ", function.Tests))}); to see it work: {(function.Steps.Length == 0 ? "no clicks listed" : string.Join(" > ", function.Steps))}{(function.Walked ? string.Empty : "  [not walked in a browser]")}");
        }

        foreach (var file in plan.NotMentioned)
        {
            Console.WriteLine($"  no test is about anything in {file}");
        }

        var browser = options.Flag("--browser") && !options.Flag("--plan") ? await live.RunStageAsync("quick: browser journeys", "Browser journeys of the changed functions", () => BrowserOfChanged(root, config, plan)) : 0;
        if (options.Flag("--no-tests"))
        {
            return browser;   // --no-tests --browser: just the journeys
        }

        if (plan.ClassesByProject.Count == 0)
        {
            if (browser != 0)
            {
                return browser;
            }

            Console.WriteLine("  No test class selected - nothing to run. (Reached only through the running app? The full suite covers it at the push.)");
            return 0;
        }

        if (options.Flag("--plan"))
        {
            return PrintPlan(root, config, plan);
        }

        return Math.Max(browser, await RunSelectedAsync(root, options, plan, live));
    }

    /// <summary>Runs each project's selected test classes (just those, by filter) and reports each project's own summary line.</summary>
    private static async Task<int> RunSelectedAsync(string root, Options options, TestPlan plan, LiveRun live)
    {
        foreach (var project in plan.ClassesByProject.Keys)
        {
            var declared = Path.GetFileNameWithoutExtension(project);
            live.Declare(LiveSteps.QuickTests(declared), $"Tests: {declared} (the ones for the change)");
        }

        var worst = 0;
        foreach (var (project, classes) in plan.ClassesByProject)
        {
            var name = Path.GetFileNameWithoutExtension(project);
            worst = Math.Max(worst, await live.RunStageAsync(LiveSteps.QuickTests(name), $"Tests: {name} (the ones for the change)", () => RunSelectedProjectAsync(root, options, project, classes, live)));
        }

        return worst;
    }

    private static async Task<int> RunSelectedProjectAsync(string root, Options options, string project, IReadOnlyCollection<string> classes, LiveRun live)
    {
        var name = Path.GetFileNameWithoutExtension(project);
        Console.WriteLine($"Running {name}: {classes.Count} test class(es)...");
        var noBuild = options.Flag("--no-build") ? " --no-build" : string.Empty;
        var (code, output) = await Shell.RunAsync("dotnet", $"test \"{project}\" --nologo -v q{noBuild} --filter \"{TestSelection.Filter(classes)}\"", root, 1800);
        var summary = output.Split('\n').LastOrDefault(l => l.Contains("Passed!", StringComparison.Ordinal) || l.Contains("Failed!", StringComparison.Ordinal))?.Trim();
        Console.WriteLine("  " + (summary ?? (code == 0 ? "passed" : "did not complete")));
        live.Counts(LiveSteps.QuickTests(name), TestSummaryLine.Parse(summary));
        return code == 0 ? 0 : summary is null ? 2 : 1;
    }

    /// <summary>The generated journeys are driven function by function (<see cref="BrowserOfChanged"/>); selecting their test class by name as well would run every journey, which is minutes of browser for a small change.</summary>
    private static TestPlan WithoutGeneratedJourneys(string root, ToolConfig config, TestPlan plan)
    {
        var generated = Path.GetFullPath(BrowserCommand.ProjectFolder(root, config)) + Path.DirectorySeparatorChar;
        var kept = plan.ClassesByProject.Where(p => !Path.GetFullPath(p.Key).StartsWith(generated, StringComparison.OrdinalIgnoreCase)).ToDictionary(p => p.Key, p => p.Value);
        return plan with { ClassesByProject = kept };
    }

    /// <summary>--browser: drives the generated journeys of just the functions the change touches in a real browser (see <c>quality browser --generate</c>). Skipped, with the reason, where there is no such project.</summary>
    private static async Task<int> BrowserOfChanged(string root, ToolConfig config, TestPlan plan)
    {
        var folder = BrowserCommand.ProjectFolder(root, config);
        var project = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.csproj").FirstOrDefault() : null;
        if (project is null || plan.Functions.Count == 0)
        {
            Console.WriteLine(project is null ? "  --browser: no generated browser tests here (run `quality browser --generate`)." : "  --browser: the change touches no function, so no journey to drive.");
            return 0;
        }

        Console.WriteLine($"Driving {plan.Functions.Count} journey(s) in a browser: {string.Join(", ", plan.Functions.Select(f => f.Id))}...");
        var (code, output) = await Shell.RunAsync("dotnet", $"test \"{project}\" --nologo -v q --filter \"{BrowserCommand.Filter(plan.Functions, config.Browser?.Shards ?? 1)}\"", root, 1800);
        var summary = output.Split('\n').LastOrDefault(l => l.Contains("Passed!", StringComparison.Ordinal) || l.Contains("Failed!", StringComparison.Ordinal))?.Trim();
        Console.WriteLine("  " + (summary ?? (code == 0 ? "passed" : "did not complete")));
        return code == 0 ? 0 : summary is null ? 2 : 1;
    }

    /// <summary>--plan: says which test classes would run, of how many each project has, without running them - the number to judge a by-name selection by before trusting it as a gate.</summary>
    private static int PrintPlan(string root, ToolConfig config, TestPlan plan)
    {
        var index = FeatureFunctions.IndexTests(FeatureMap.ReadTests(root, config)).Where(c => c.Tests.Count > 0).ToList();
        foreach (var (project, classes) in plan.ClassesByProject)
        {
            var name = Path.GetFileNameWithoutExtension(project);
            var inProject = index.Where(c => c.Project == name).ToList();
            var selected = inProject.Where(c => classes.Contains(c.Name)).Sum(c => c.Tests.Count);
            Console.WriteLine($"PLAN {name}: {selected} of {inProject.Sum(c => c.Tests.Count)} tests ({classes.Count} of {inProject.Count} classes)");
            foreach (var group in classes.GroupBy(c => plan.Why.TryGetValue(c, out var why) ? why : "selected").OrderByDescending(g => g.Count()))
            {
                Console.WriteLine($"  {group.Count(),3} class(es): {group.Key}: {string.Join(", ", group.Take(6))}{(group.Count() > 6 ? ", ..." : string.Empty)}");
            }
        }

        return 0;
    }

    /// <summary>A baseline key is rule|file|member.</summary>
    private static string FileOf(string key)
    {
        var parts = key.Split('|');
        return parts.Length > 1 ? parts[1] : string.Empty;
    }
}
