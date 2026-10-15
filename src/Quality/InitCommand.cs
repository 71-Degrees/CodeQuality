/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>
/// <c>quality init</c>: the first-run autopilot. It does every outstanding first-run step C# can do with no tokens and nothing that touches a live site, one line each, and then lists what is left for a person or
/// Claude. Safe to run again: a step that is done is not repeated, and nothing existing is rewritten.
/// </summary>
internal static class InitCommand
{
    /// <summary>The steps that run themselves, by checklist id. Each returns what it did, in a line.</summary>
    private static readonly IReadOnlyDictionary<string, Func<string, ToolConfig, Task<string>>> Steps = new Dictionary<string, Func<string, ToolConfig, Task<string>>>(StringComparer.Ordinal)
    {
        ["reports-ignored"] = (root, _) => Task.FromResult(IgnoreReports(root)),
        ["analyzers-on"] = (root, config) => Task.FromResult(SwitchAnalyzersOn(root, config)),
        ["features"] = GenerateFeaturesAsync,
        ["browser-tests"] = (root, config) => Task.FromResult(GenerateBrowserTests(root, config)),
    };

    /// <summary>Accepting a baseline is the owner's call, so these two run only when asked for with <c>--accept-baselines</c> (a brand-new repository has almost nothing to accept).</summary>
    private static readonly IReadOnlyDictionary<string, Func<string, ToolConfig, Task<string>>> OnRequest = new Dictionary<string, Func<string, ToolConfig, Task<string>>>(StringComparer.Ordinal)
    {
        ["baseline"] = (root, config) => Task.FromResult(AcceptSmells(root, config)),
        ["analyzer-baseline"] = AcceptAnalyzersAsync,
    };

    internal static async Task<int> RunAsync(string root, ToolConfig config, bool acceptBaselines = false)
    {
        var file = SetupFile.Load(root);
        var facts = new SetupFacts(root, config, Collected.Load(root), TestResults.Read(root, config), file);
        var outstanding = SetupChecklist.Outstanding(SetupChecklist.Evaluate(facts));
        var did = 0;
        foreach (var status in outstanding.OrderBy(s => s.Item.Id == "browser-tests" ? 1 : 0))   // the browser tests are drawn from the feature list, so it comes first
        {
            var step = Steps.GetValueOrDefault(status.Item.Id) ?? (acceptBaselines ? OnRequest.GetValueOrDefault(status.Item.Id) : null);
            if (step is null)
            {
                continue;
            }

            Console.WriteLine($"  {status.Item.Id,-16} {await step(root, config)}");
            did++;
        }

        Console.WriteLine(did == 0 ? "Init: nothing to do automatically." : $"Init: {did} step(s) done.");
        var rest = SetupChecklist.Outstanding(SetupChecklist.Evaluate(new SetupFacts(root, config, Collected.Load(root), TestResults.Read(root, config), SetupFile.Load(root))));
        Console.WriteLine(SetupCommand.Render(rest, config, SiteSize.Measure(root, config), firstRun: false));
        return 0;
    }

    internal static string IgnoreReports(string root)
    {
        var path = Path.Combine(root, ".gitignore");
        var existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        File.WriteAllText(path, existing + (existing.Length == 0 || existing.EndsWith('\n') ? string.Empty : Environment.NewLine) + "reports/" + Environment.NewLine);
        return "added reports/ to .gitignore";
    }

    /// <summary>Turns the .NET analyzers on in every build as warnings, quiet about the same rules the tool ignores. Edits Directory.Build.props (creating it if there is none) and never one that already sets AnalysisMode.</summary>
    internal static string SwitchAnalyzersOn(string root, ToolConfig config)
    {
        var path = Path.Combine(root, "Directory.Build.props");
        var block = $"""
                <!-- The .NET analyzers run in every build as warnings (the ratchet in quality/baseline.json is what fails a push); quiet about the rules `quality` ignores too. -->
                <EnableNETAnalyzers>true</EnableNETAnalyzers>
                <AnalysisMode>Recommended</AnalysisMode>
                <NoWarn>$(NoWarn);{string.Join(';', Analyzers.IgnoredCodes(config))}</NoWarn>
            """;
        if (!File.Exists(path))
        {
            File.WriteAllText(path, $"<Project>{Environment.NewLine}  <PropertyGroup>{Environment.NewLine}{block}{Environment.NewLine}  </PropertyGroup>{Environment.NewLine}</Project>{Environment.NewLine}");
            return "created Directory.Build.props with the analyzers on";
        }

        var text = File.ReadAllText(path);
        var close = text.IndexOf("</PropertyGroup>", StringComparison.Ordinal);
        if (close < 0)
        {
            return "Directory.Build.props has no PropertyGroup - add AnalysisMode=Recommended by hand";
        }

        File.WriteAllText(path, text.Insert(close, block + Environment.NewLine + "  "));
        return "added AnalysisMode=Recommended to Directory.Build.props";
    }

    private static string GenerateBrowserTests(string root, ToolConfig config)
    {
        var (ok, message) = BrowserCommand.Generate(root, config);
        return ok ? message + " (add the project to the solution: dotnet sln add)" : "not generated - " + message;
    }

    internal static string AcceptSmells(string root, ToolConfig config)
    {
        var findings = Smells.Scan(root, includeTests: false, config);
        Accept(root, findings, key => !key.StartsWith("build/", StringComparison.Ordinal));
        return $"accepted {findings.Count} smell finding(s) as the baseline";
    }

    private static async Task<string> AcceptAnalyzersAsync(string root, ToolConfig config)
    {
        (IReadOnlyList<Finding> findings, string? problem) result;
        try
        {
            result = await Analyzers.RunAsync(root, "Recommended", config);
        }
        catch (InvalidOperationException ex)
        {
            return "not accepted - " + ex.Message.Split('.')[0];   // no solution configured yet
        }

        var (findings, problem) = result;
        if (problem is not null)
        {
            return "not accepted - " + problem.Split('\n')[0];
        }

        Accept(root, findings, key => key.StartsWith("build/", StringComparison.Ordinal));
        return $"accepted {findings.Count} analyzer finding(s) as the baseline";
    }

    /// <summary>Replaces this check's entries in quality/baseline.json and leaves the other check's alone (the same merge <c>--update-baseline</c> does).</summary>
    private static void Accept(string root, IReadOnlyList<Finding> findings, Func<string, bool> ownKeys)
    {
        var kept = Baseline.Load(root).Entries.Where(e => !ownKeys(e.Key)).ToDictionary(e => e.Key, e => e.Value);
        foreach (var (key, value) in Baseline.From(findings).Entries)
        {
            kept[key] = value;
        }

        new Baseline { Entries = kept }.Save(root);
    }

    private static async Task<string> GenerateFeaturesAsync(string root, ToolConfig config)
    {
        var (path, proposed, drafted) = await FeatureGenerator.WriteAsync(root, config);
        return proposed ? $"features.json exists; {drafted.Functions.Count} draft(s) for uncovered pages in {Path.GetRelativePath(root, path)}" : $"drafted {drafted.Functions.Count} function(s) into {Path.GetRelativePath(root, path)}";
    }
}
