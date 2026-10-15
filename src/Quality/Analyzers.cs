/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.RegularExpressions;
using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>
/// The compiler's own analyzers, turned up. A normal build only shows the handful of rules Microsoft enables by default; <c>AnalysisMode=Recommended</c> adds the rest of the
/// .NET code-quality rules (CA*), and <c>EnforceCodeStyleInBuild</c> adds the style rules (IDE*) that are configured. The warnings are read from the build output and compared with
/// the baseline per rule and file, so a new warning fails the check while the old ones are worked down at leisure.
/// </summary>
internal static class Analyzers
{
    /// <summary>Rules that rarely fit an application, so they are not counted. Each has a reason; a repository adds its own in quality/quality.json (ignoredAnalyzerRules).</summary>
    private static readonly Dictionary<string, string> Ignored = new()
    {
        ["CA1707"] = "tests are named as readable sentences with underscores on purpose",
        ["CA1848"] = "wants a LoggerMessage delegate for every log call: a micro-optimisation that is not worth the ceremony at this volume",
        ["CA1873"] = "same family as CA1848 (avoid evaluating log arguments)",
        ["CA1861"] = "constant array arguments: micro-allocation, not a quality problem",
        ["CA1716"] = "identifier names that clash with keywords in other languages: irrelevant for an app (not a library)",
        ["CA1711"] = "type-name suffix conventions: irrelevant for an app (not a library)",
    };

    /// <summary>The rule codes a build should stay quiet about for this site: the tool's own list plus the site's <c>ignoredAnalyzerRules</c>.</summary>
    internal static IReadOnlyList<string> IgnoredCodes(ToolConfig config) => [.. Ignored.Keys.Concat(config.IgnoredAnalyzerRules.Keys).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static readonly Regex Warning = new(@"^(?<file>[A-Za-z]:[^()]*?)\((?<line>\d+),\d+\): warning (?<code>[A-Z]+\d+): (?<message>.*?) \[", RegexOptions.Compiled | RegexOptions.Multiline);

    public static async Task<(IReadOnlyList<Finding> Findings, string? Problem)> RunAsync(string root, string mode, ToolConfig config)
    {
        // --no-incremental: an incremental build skips the compiler, and a skipped compiler reports no warnings.
        // The build goes to a scratch folder: this tool is itself one of the projects, and a running program cannot have its own files overwritten.
        var scratch = Path.Combine(Path.GetTempPath(), "tn-quality-build").Replace('\\', '/') + "/";   // forward slashes: a trailing backslash would escape the closing quote
        var args = $"build \"{config.SolutionPath(root)}\" --no-incremental -nologo -v:q -p:AnalysisMode={mode} -p:EnforceCodeStyleInBuild=true -p:TreatWarningsAsErrors=false -p:RunAnalyzersDuringBuild=true -p:BaseOutputPath=\"{scratch}\"";
        var (code, output) = await Shell.RunAsync("dotnet", args, root);
        var found = new Dictionary<string, Finding>();
        var counts = new Dictionary<string, int>();
        var seen = new HashSet<string>();
        foreach (Match m in Warning.Matches(output))
        {
            var file = Path.GetRelativePath(root, m.Groups["file"].Value.Trim()).Replace('\\', '/');
            var line = int.Parse(m.Groups["line"].Value);
            if (Ignored.ContainsKey(m.Groups["code"].Value) || config.IgnoredAnalyzerRules.ContainsKey(m.Groups["code"].Value))
            {
                continue;
            }

            var rule = "build/" + m.Groups["code"].Value;
            if (file.StartsWith("..") || !seen.Add($"{rule}|{file}|{line}|{m.Groups["message"].Value}"))
            {
                continue;   // outside the repo, or the same warning printed again for a second target framework
            }

            var key = $"{rule}|{file}|(file)";
            counts[key] = counts.GetValueOrDefault(key) + 1;
            found[key] = new Finding(rule, file, "(file)", found.TryGetValue(key, out var first) ? first.Line : line, counts[key],
                $"{counts[key]} x {m.Groups["code"].Value} in this file, for example line {(found.TryGetValue(key, out var f2) ? f2.Line : line)}: {m.Groups["message"].Value}");
        }

        if (found.Count == 0 && code != 0)
        {
            return (Array.Empty<Finding>(), "The build failed, so no analysis was produced:\n" + string.Join('\n', output.Split('\n').Where(l => l.Contains("error")).Take(8)));
        }

        return (found.Values.ToList(), null);
    }
}

/// <summary>Runs the type check and the eslint rules for the browser scripts (the same two the pre-push check runs).</summary>
internal static class Scripts
{
    public static async Task<(bool Ok, string Summary)> RunAsync(string root, ToolConfig config)
    {
        if (config.ScriptChecks.Length == 0)
        {
            return (true, "not configured (add scriptChecks to quality/quality.json to run the npm type check or lint)");
        }

        var nodeDir = new[] { Environment.GetEnvironmentVariable("ProgramFiles") + @"\nodejs" }.FirstOrDefault(Directory.Exists);
        var path = nodeDir is null ? Environment.GetEnvironmentVariable("PATH") : nodeDir + ";" + Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", path);
        if (!Directory.Exists(Path.Combine(root, "node_modules")))
        {
            var (installCode, installOut) = await Shell.RunAsync("cmd.exe", "/c npm install --no-audit --no-fund", root, 300);
            if (installCode != 0)
            {
                return (false, "Node/npm is not available (npm install failed): " + installOut.Trim().Split('\n').LastOrDefault());
            }
        }

        var results = new List<string>();
        var ok = true;
        foreach (var script in config.ScriptChecks)
        {
            var (code, output) = await Shell.RunAsync("cmd.exe", $"/c npm run {script} --silent", root, 300);
            results.Add($"{script}: {(code == 0 ? "clean" : "FAILED")}");
            if (code != 0)
            {
                ok = false;
                results.AddRange(output.Split('\n').Where(l => l.Trim().Length > 0).Take(12).Select(l => "    " + l.TrimEnd()));
            }
        }

        return (ok, string.Join('\n', results));
    }
}
