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
    /// <summary>The browser tests run with --include-browser, unless --browser-classes none says every class already has a pass to reuse (the caller, ship, keeps that record).</summary>
    internal static bool IncludeBrowser(Options options) => options.Flag("--include-browser") && options.Text("--browser-classes", string.Empty) != "none";

    /// <summary>Runs only these browser test classes (--browser-classes A,B: the ones with no earlier pass to reuse, picked by ship); without the option they all run.</summary>
    private static string BrowserFilter(Options options, string name) =>
        TestResults.IsBrowserProject(name) && options.Text("--browser-classes", string.Empty) is { Length: > 0 } classes && classes != "none" ? $" --filter \"{string.Join('|', classes.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(c => $"FullyQualifiedName~.{c}."))}\"" : string.Empty;

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
    /// Runs each project, one after another, quick ones first. Stops at the first project that fails (the push is lost anyway, and the rest only costs minutes); <c>--keep-going</c> runs them all.
    /// Returns the exit code: 0 when every test passed, 1 when any failed, 2 when a project could not be run at all.
    /// </summary>
    internal static async Task<int> RunAsync(RunContext context)
    {
        var (root, options, say, _, config) = context;
        var projects = Projects(root, config, IncludeBrowser(options));
        if (projects.Count == 0)
        {
            say("No test projects found (set testProjects in quality/quality.json).");
            return 2;
        }

        var worst = 0;
        foreach (var project in projects)
        {
            var name = Path.GetFileNameWithoutExtension(project);
            worst = Math.Max(worst, await context.Live.RunStageAsync(LiveSteps.Tests(name), $"Tests: {name}", () => RunProjectAsync(context, project)));
            if (worst > 0 && !options.Flag("--keep-going"))
            {
                SkipRest(context, projects.Skip(projects.IndexOf(project) + 1).ToList());
                break;
            }
        }

        return worst;
    }

    // One project with the coverage collector: 0 passed, 1 some test failed, 2 it did not run at all (no results file).
    private static async Task<int> RunProjectAsync(RunContext context, string project)
    {
        var (root, options, say, _, _) = context;
        var name = Path.GetFileNameWithoutExtension(project);
        var results = Path.Combine(TestResults.ResultsFolder(root), name);
        ClearPreviousResults(results);

        say($"Running {name}...");
        var (code, output) = await Shell.RunAsync(
            "dotnet",
            $"test \"{project}\" --nologo -v q --collect:\"XPlat Code Coverage\" --logger \"trx;LogFileName=results.trx\" --results-directory \"{results}\"{BrowserFilter(options, name)}",
            root,
            1800);
        var summary = output.Split('\n').LastOrDefault(l => l.Contains("Passed!", StringComparison.Ordinal) || l.Contains("Failed!", StringComparison.Ordinal))?.Trim();
        say("  " + (summary ?? (code == 0 ? "passed" : "did not complete")));
        context.Live.Counts(LiveSteps.Tests(name), TestSummaryLine.Parse(summary));
        return code == 0 ? 0 : File.Exists(Path.Combine(results, "results.trx")) ? 1 : 2;
    }

    // The projects not run keep no results from an earlier run: the report would mix this push's failure with another push's numbers.
    private static void SkipRest(RunContext context, List<string> skipped)
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
        context.Live.Skip(names.Select(n => (LiveSteps.Tests(n), $"Tests: {n}")), "not run: an earlier project failed");
        context.Say($"Stopped at the first failing project; not run: {string.Join(", ", names)} (--keep-going runs them all).");
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
