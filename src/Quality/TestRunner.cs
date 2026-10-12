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
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Runs each project, one after another. Returns the exit code: 0 when every test passed, 1 when any failed, 2 when a project could not be run at all.</summary>
    internal static async Task<int> RunAsync(RunContext context)
    {
        var (root, options, say, _, config) = context;
        var projects = Projects(root, config, options.Flag("--include-browser"));
        if (projects.Count == 0)
        {
            say("No test projects found (set testProjects in quality/quality.json).");
            return 2;
        }

        var worst = 0;
        foreach (var project in projects)
        {
            var name = Path.GetFileNameWithoutExtension(project);
            var results = Path.Combine(TestResults.ResultsFolder(root), name);
            ClearPreviousResults(results);

            say($"Running {name}...");
            var (code, output) = await Shell.RunAsync(
                "dotnet",
                $"test \"{project}\" --nologo -v q --collect:\"XPlat Code Coverage\" --logger \"trx;LogFileName=results.trx\" --results-directory \"{results}\"",
                root,
                1800);
            var summary = output.Split('\n').LastOrDefault(l => l.Contains("Passed!", StringComparison.Ordinal) || l.Contains("Failed!", StringComparison.Ordinal))?.Trim();
            say("  " + (summary ?? (code == 0 ? "passed" : "did not complete")));
            worst = Math.Max(worst, code == 0 ? 0 : File.Exists(Path.Combine(results, "results.trx")) ? 1 : 2);
        }

        return worst;
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
