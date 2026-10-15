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
    };

    internal static async Task<int> RunAsync(string root, ToolConfig config)
    {
        var file = SetupFile.Load(root);
        var facts = new SetupFacts(root, config, Collected.Load(root), TestResults.Read(root, config), file);
        var outstanding = SetupChecklist.Outstanding(SetupChecklist.Evaluate(facts));
        var did = 0;
        foreach (var status in outstanding.Where(s => Steps.ContainsKey(s.Item.Id)))
        {
            Console.WriteLine($"  {status.Item.Id,-16} {await Steps[status.Item.Id](root, config)}");
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

    private static async Task<string> GenerateFeaturesAsync(string root, ToolConfig config)
    {
        var (path, proposed, drafted) = await FeatureGenerator.WriteAsync(root, config);
        return proposed ? $"features.json exists; {drafted.Functions.Count} draft(s) for uncovered pages in {Path.GetRelativePath(root, path)}" : $"drafted {drafted.Functions.Count} function(s) into {Path.GetRelativePath(root, path)}";
    }
}
