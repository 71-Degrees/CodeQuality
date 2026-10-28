/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>Runs the site's test projects with coverage and writes their results where <see cref="TestResults"/> reads them (<c>quality tests</c>).</summary>
internal static class TestRunner
{
    /// <summary>The projects to run: <see cref="Projects"/>, less the browser project whose classes all have a pass to reuse (--browser-classes none; the caller, ship, keeps that record).</summary>
    internal static List<string> Selected(string root, ToolConfig config, Options options) =>
        Projects(root, config, options.Flag("--include-browser"))
            .Where(p => !(options.Text("--browser-classes", string.Empty) == "none" && ClassesApplyTo(options, Path.GetFileNameWithoutExtension(p))))
            .ToList();

    // --browser-classes-in <project> names the one browser project the class list belongs to; without it the list applies to every browser project.
    private static bool ClassesApplyTo(Options options, string project) =>
        TestResults.IsBrowserProject(project) && options.Text("--browser-classes-in", project) == project;

    /// <summary>Runs only these browser test classes (--browser-classes A,B: the ones with no earlier pass to reuse, picked by ship); without the option they all run.</summary>
    private static string BrowserFilter(Options options, string name) =>
        ClassesApplyTo(options, name) && options.Text("--browser-classes", string.Empty) is { Length: > 0 } classes && classes != "none" ? $" --filter \"{string.Join('|', classes.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(c => $"FullyQualifiedName~.{c}."))}\"" : string.Empty;

    /// <summary>The test projects to run: the configured ones, else every project one folder down under each test folder. Browser tests (a folder with "Browser" in its name) only run with --include-browser: they start the site and a browser.</summary>
    internal static List<string> Projects(string root, ToolConfig config, bool includeBrowser)
    {
        var configured = config.TestProjects;
        var found = configured is { Length: > 0 }
            ? configured.Select(p => Path.Combine(root, p)).SelectMany(p => File.Exists(p) ? [p] : Directory.Exists(p) ? Directory.EnumerateFiles(p, "*.csproj") : [])
            : config.TestFolders.Select(f => Path.Combine(root, f)).Where(Directory.Exists)
                .SelectMany(Directory.EnumerateDirectories).SelectMany(d => Directory.EnumerateFiles(d, "*.csproj"));
        return found
            .Where(p => includeBrowser || !TestResults.IsBrowserProject(Path.GetFileNameWithoutExtension(p)))
            .OrderBy(p => TestResults.IsBrowserProject(Path.GetFileNameWithoutExtension(p)))   // quick projects first, so a failure shows in seconds and not after the browser tests
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Runs the test projects, quick ones first. Stops at the first project that fails (the push is lost anyway, and the rest only costs minutes); <c>--keep-going</c> runs them all.
    /// With <c>prebuildTests</c> the solution is built once first and every project runs with --no-build; with <c>parallelBrowserProjects</c> the browser projects run side by side.
    /// Returns the exit code: 0 when every test passed, 1 when any failed, 2 when a project could not be run at all.
    /// </summary>
    internal static async Task<int> RunAsync(RunContext context)
    {
        var (root, options, say, _, config) = context;
        var projects = Selected(root, config, options);
        if (projects.Count == 0)
        {
            say("No test projects found (set testProjects in quality/quality.json).");
            return 2;
        }

        var prebuilt = config.PrebuildTests && !options.Flag("--no-prebuild");
        if (prebuilt && await PrebuildAsync(context) != 0)
        {
            SkipRest(context, projects, "the build failed");
            return 2;
        }

        var units = projects.Where(p => !TestResults.IsBrowserProject(Path.GetFileNameWithoutExtension(p))).ToList();
        var browsers = projects.Where(p => TestResults.IsBrowserProject(Path.GetFileNameWithoutExtension(p))).ToList();
        if (config.ParallelUnitsWithBrowsers && browsers.Count > 0)
        {
            // The browser projects do not wait for the units, so a unit failure cannot skip them (and --stop-browser-after-unit-failure has nothing to stop).
            var both = await Task.WhenAll(RunInOrderAsync(context, units, prebuilt), RunBrowserProjectsAsync(context, browsers, prebuilt));
            return both.Max();
        }

        var worst = await RunInOrderAsync(context, units, prebuilt);
        if (worst > 0 && (!options.Flag("--keep-going") || options.Flag("--stop-browser-after-unit-failure")))
        {
            // The second flag is opt-in: a resumed run reruns just the failed tests, so browser projects skipped here would not run again.
            SkipRest(context, browsers, "a unit test project failed");
            return worst;
        }

        return Math.Max(worst, await RunBrowserProjectsAsync(context, browsers, prebuilt));
    }

    private static Task<int> RunBrowserProjectsAsync(RunContext context, List<string> browsers, bool prebuilt) =>
        browsers.Count > 1 && context.Config.ParallelBrowserProjects ? RunBrowsersTogetherAsync(context, browsers, prebuilt) : RunInOrderAsync(context, browsers, prebuilt);

    // One after another; stops at the first failure unless --keep-going (the rest is then marked not run).
    private static async Task<int> RunInOrderAsync(RunContext context, List<string> projects, bool noBuild)
    {
        var worst = 0;
        foreach (var project in projects)
        {
            var name = Path.GetFileNameWithoutExtension(project);
            worst = Math.Max(worst, await context.Live.RunStageAsync(LiveSteps.Tests(name), $"Tests: {name}", () => RunProjectAsync(context, project, noBuild)));
            if (worst > 0 && !context.Options.Flag("--keep-going"))
            {
                SkipRest(context, projects.Skip(projects.IndexOf(project) + 1).ToList(), "an earlier project failed");
                break;
            }
        }

        return worst;
    }

    // The browser projects side by side. The shared pre-build runs first, once, so the sites they start find the web project already built.
    private static async Task<int> RunBrowsersTogetherAsync(RunContext context, List<string> browsers, bool noBuild)
    {
        foreach (var arguments in context.Config.BrowserPrebuild ?? [])
        {
            context.Say($"Before the browser tests: dotnet {arguments}");
            var (code, output) = await Shell.RunAsync("dotnet", arguments, context.Root, 1800);
            if (code != 0)
            {
                context.Say("  failed: " + output.Split('\n').LastOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase))?.Trim());
                SkipRest(context, browsers, "the browser pre-build failed");
                return 2;
            }
        }

        var results = await Task.WhenAll(browsers.Select(project =>
        {
            var name = Path.GetFileNameWithoutExtension(project);
            return context.Live.RunStageAsync(LiveSteps.Tests(name), $"Tests: {name}", () => RunProjectAsync(context, project, noBuild));
        }));
        return results.Max();
    }

    // The whole solution once; a compile error then stops the run here, in about a minute, instead of costing the tests' time to find.
    private static async Task<int> PrebuildAsync(RunContext context)
    {
        var solution = context.Config.SolutionPath(context.Root);
        context.Say("Building the solution once for all the test projects...");
        var code = await context.Live.RunStageAsync("build-tests", "Build for the tests", async () =>
            (await Shell.RunAsync("dotnet", $"build \"{solution}\" --nologo -v q", context.Root, 1800)).Code == 0 ? 0 : 2);
        if (code != 0)
        {
            context.Say("  the build failed; run `dotnet build` to see the first errors.");
        }

        return code;
    }

    // One project with the coverage collector: 0 passed, 1 some test failed, 2 it did not run at all (no results file).
    private static async Task<int> RunProjectAsync(RunContext context, string project, bool noBuild)
    {
        var (root, options, say, _, _) = context;
        var name = Path.GetFileNameWithoutExtension(project);
        var results = Path.Combine(TestResults.ResultsFolder(root), name);
        ClearPreviousResults(results);

        say($"Running {name}...");
        var (code, output) = await Shell.RunAsync(
            "dotnet",
            $"test \"{project}\" --nologo -v q{(noBuild ? " --no-build" : string.Empty)} --collect:\"XPlat Code Coverage\" --logger \"trx;LogFileName=results.trx\" --results-directory \"{results}\"{BrowserFilter(options, name)}",
            root,
            1800);
        var summary = output.Split('\n').LastOrDefault(l => l.Contains("Passed!", StringComparison.Ordinal) || l.Contains("Failed!", StringComparison.Ordinal))?.Trim();
        say("  " + (summary ?? (code == 0 ? "passed" : "did not complete")));
        if (summary is null && code != 0)
        {
            // A project that died without a result line used to be one word on the screen; its own last lines say why (a missing build, a port in use, a crashed test host).
            foreach (var line in output.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).TakeLast(8))
            {
                say("    | " + line);
            }
        }

        context.Live.Counts(LiveSteps.Tests(name), TestSummaryLine.Parse(summary));
        return code == 0 ? 0 : File.Exists(Path.Combine(results, "results.trx")) ? 1 : 2;
    }

    // The projects not run keep no results from an earlier run: the report would mix this push's failure with another push's numbers.
    private static void SkipRest(RunContext context, List<string> skipped, string reason)
    {
        if (skipped.Count == 0)
        {
            return;
        }

        foreach (var project in skipped)
        {
            ClearPreviousResults(Path.Combine(TestResults.ResultsFolder(context.Root), Path.GetFileNameWithoutExtension(project)));
        }

        var names = skipped.Select(p => Path.GetFileNameWithoutExtension(p)).ToList();
        context.Live.Skip(names.Select(n => (LiveSteps.Tests(n), $"Tests: {n}")), $"not run: {reason}");
        context.Say($"Not run ({reason}): {string.Join(", ", names)} (--keep-going runs them all).");
    }

    // Each run replaces the project's own results (regenerated output, like bin and obj): the Cobertura file is written into a new folder per run, so the old ones would pile up and be read as one.
    private static void ClearPreviousResults(string folder)
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
