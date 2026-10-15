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
    internal static async Task<int> RunAsync(string root, Options options, ToolConfig config)
    {
        var clock = Stopwatch.StartNew();
        var since = options.Text("--since", string.Empty);
        var commit = options.Text("--commit", string.Empty);
        var files = commit.Length > 0 ? await ChangedFiles.OfCommitAsync(root, commit) : await ChangedFiles.FindAsync(root, since.Length == 0 ? null : since);
        var changed = ChangedFiles.Sources(files, config);
        Console.WriteLine($"Quick check of what changed since {(since.Length == 0 ? "HEAD" : since)}: {changed.Count} C#/Razor file(s).");
        if (changed.Count == 0)
        {
            Console.WriteLine("Nothing to check. (The full check - `quality` and the test suite - is for the push.)");
            return 0;
        }

        foreach (var file in changed)
        {
            Console.WriteLine($"  {(file.IsTest ? "test  " : "source")} {file.Relative}");
        }

        // --no-smells: the push check already ran the whole-repository smells ratchet, so only the tests are wanted here.
        var worst = options.Flag("--no-smells") ? 0 : SmellsOfChanged(root, options, config, changed);
        if (!options.Flag("--no-tests") || options.Flag("--browser"))
        {
            worst = Math.Max(worst, await TestsOfChanged(root, options, config, changed));
        }

        Console.WriteLine();
        Console.WriteLine($"{(worst == 0 ? "Quick check clean" : worst == 1 ? "Something new, worse or failing needs a look" : "A check could not run")} ({clock.Elapsed.TotalSeconds:0.#} s). The full check and the whole test suite are still the gate before a push.");
        return worst;
    }

    private static int SmellsOfChanged(string root, Options options, ToolConfig config, IReadOnlyList<ChangedSource> changed)
    {
        Console.WriteLine();
        Console.WriteLine("== Code smells in the changed files");
        var paths = changed.Select(c => c.Relative).ToHashSet(StringComparer.Ordinal);
        var findings = Smells.Scan(root, options.Flag("--include-tests"), config).Where(f => paths.Contains(f.File)).ToList();
        var baseline = Baseline.Load(root);
        var comparison = baseline.Compare(findings, key => !key.StartsWith("build/", StringComparison.Ordinal) && paths.Contains(FileOf(key)));
        Console.WriteLine($"{findings.Count} finding(s) in them; against the baseline: {comparison.New.Count} new, {comparison.Worse.Count} worse, {comparison.Fixed.Count} fixed.");
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

    private static async Task<int> TestsOfChanged(string root, Options options, ToolConfig config, IReadOnlyList<ChangedSource> changed)
    {
        Console.WriteLine();
        Console.WriteLine("== Tests that mention the changed code");
        var projects = TestRunner.Projects(root, config, includeBrowser: false);
        var plan = TestSelection.Plan(root, changed, projects, config.SourceFolders, FeatureRegistryFile.Load(root, config.Features));
        foreach (var function in plan.Functions)
        {
            Console.WriteLine($"  function: {function.Name} ({(function.Tests.Length == 0 ? "no test is named for it" : string.Join(", ", function.Tests))}); to see it work: {(function.Steps.Length == 0 ? "no clicks listed" : string.Join(" > ", function.Steps))}{(function.Walked ? string.Empty : "  [not walked in a browser]")}");
        }

        foreach (var file in plan.NotMentioned)
        {
            Console.WriteLine($"  no test is about anything in {file}");
        }

        var browser = options.Flag("--browser") && !options.Flag("--plan") ? await BrowserOfChanged(root, config, plan) : 0;
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

        var worst = browser;
        foreach (var (project, classes) in plan.ClassesByProject)
        {
            var name = Path.GetFileNameWithoutExtension(project);
            Console.WriteLine($"Running {name}: {classes.Count} test class(es)...");
            var noBuild = options.Flag("--no-build") ? " --no-build" : string.Empty;
            var (code, output) = await Shell.RunAsync("dotnet", $"test \"{project}\" --nologo -v q{noBuild} --filter \"{TestSelection.Filter(classes)}\"", root, 1800);
            var summary = output.Split('\n').LastOrDefault(l => l.Contains("Passed!", StringComparison.Ordinal) || l.Contains("Failed!", StringComparison.Ordinal))?.Trim();
            Console.WriteLine("  " + (summary ?? (code == 0 ? "passed" : "did not complete")));
            worst = Math.Max(worst, code == 0 ? 0 : summary is null ? 2 : 1);
        }

        return worst;
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
        var (code, output) = await Shell.RunAsync("dotnet", $"test \"{project}\" --nologo -v q --filter \"{BrowserCommand.Filter(plan.Functions)}\"", root, 1800);
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
