/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;

namespace CodeQuality.Quality.Tests;

public sealed class InitCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quality-init-" + Guid.NewGuid().ToString("N"));

    public InitCommandTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Read(string relative) => File.ReadAllText(Path.Combine(_root, relative));

    [Fact]
    public void ReportsAreIgnored_AfterAnExistingLastLineWithNoNewline()
    {
        File.WriteAllText(Path.Combine(_root, ".gitignore"), "bin/");

        InitCommand.IgnoreReports(_root);

        Assert.Equal(["bin/", "reports/"], Read(".gitignore").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    [Fact]
    public void AnalyzersAreSwitchedOn_ByCreatingOrExtendingTheBuildProps_QuietAboutTheRulesTheToolIgnores()
    {
        Assert.Contains("created", InitCommand.SwitchAnalyzersOn(_root, new ToolConfig()), StringComparison.Ordinal);
        var created = Read("Directory.Build.props");
        Assert.Contains("<AnalysisMode>Recommended</AnalysisMode>", created, StringComparison.Ordinal);
        Assert.Contains("CA1707", created, StringComparison.Ordinal);

        File.WriteAllText(Path.Combine(_root, "Directory.Build.props"), "<Project>\n  <PropertyGroup>\n    <Nullable>enable</Nullable>\n  </PropertyGroup>\n</Project>\n");
        InitCommand.SwitchAnalyzersOn(_root, new ToolConfig { IgnoredAnalyzerRules = new() { ["CA9999"] = "mine" } });
        var extended = Read("Directory.Build.props");
        Assert.Contains("<Nullable>enable</Nullable>", extended, StringComparison.Ordinal);
        Assert.Contains("<AnalysisMode>Recommended</AnalysisMode>", extended, StringComparison.Ordinal);
        Assert.Contains("CA9999", extended, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSmellsBaselineIsAcceptedOnlyWhenAskedFor()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllText(Path.Combine(_root, "src", "A.cs"), "class A { void M() { } }");
        var config = new ToolConfig();

        await InitCommand.RunAsync(_root, config);
        Assert.False(File.Exists(Path.Combine(_root, "quality", "baseline.json")));

        await InitCommand.RunAsync(_root, config, acceptBaselines: true);
        Assert.True(File.Exists(Path.Combine(_root, "quality", "baseline.json")));
    }

    [Fact]
    public async Task InitDoesTheAutomaticSteps_ThenIsQuietBecauseTheyAreDone()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        File.WriteAllText(Path.Combine(_root, "src", "Index.razor"), "@page \"/\"\n<h1>Home</h1>\n");
        var config = new ToolConfig();

        Assert.Equal(0, await InitCommand.RunAsync(_root, config));

        Assert.Contains("reports/", Read(".gitignore"), StringComparison.Ordinal);
        Assert.Contains("AnalysisMode", Read("Directory.Build.props"), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_root, "quality", "features.json")));
        var again = SetupChecklist.Evaluate(new SetupFacts(_root, config, new Collected(), null, SetupFile.Load(_root)));
        Assert.All(again.Where(s => s.Item.Id is "reports-ignored" or "analyzers-on" or "features"), s => Assert.Equal(SetupState.Done, s.State));
    }
}
