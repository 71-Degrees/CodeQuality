/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;
using CodeQuality.Quality;

namespace CodeQuality.Quality.Tests;

public sealed class ToolConfigTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cq-config-" + Guid.NewGuid().ToString("N"));

    public ToolConfigTests() => Directory.CreateDirectory(Path.Combine(_root, "quality"));

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private void WriteConfig(string json) => File.WriteAllText(ToolConfig.PathFor(_root), json);

    private void WriteSource(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void WithNoFileTheDefaultsApply()
    {
        File.Delete(ToolConfig.PathFor(_root));

        var config = ToolConfig.Load(_root);

        Assert.Equal(new[] { "src", "tools" }, config.SourceFolders);
        Assert.Empty(config.ScriptChecks);
        Assert.Equal(60, config.Limits.MethodLines);
        Assert.Null(config.Crawl.DefaultUrl);
    }

    [Fact]
    public void ASettingInTheFileReplacesTheDefaultAndTheRestStay()
    {
        WriteConfig("""{ "siteName": "Shop", "limits": { "methodLines": 30 }, "crawl": { "defaultUrl": "https://staging.example.com" }, "scriptChecks": ["lint"] } // comment""");

        var config = ToolConfig.Load(_root);

        Assert.Equal("Shop", config.Title(_root));
        Assert.Equal(30, config.Limits.MethodLines);
        Assert.Equal(15, config.Limits.Complexity);
        Assert.Equal("https://staging.example.com", config.Crawl.DefaultUrl);
        Assert.Equal(new[] { "lint" }, config.ScriptChecks);
    }

    [Fact]
    public void TheTitleFallsBackToTheFolderName()
    {
        Assert.Equal(new DirectoryInfo(_root).Name, new ToolConfig().Title(_root));
    }

    [Fact]
    public void TheOnlySolutionAtTheRootIsFoundAndSeveralNeedAChoice()
    {
        File.WriteAllText(Path.Combine(_root, "App.slnx"), "<Solution />");
        Assert.Equal(Path.Combine(_root, "App.slnx"), new ToolConfig().SolutionPath(_root));

        File.WriteAllText(Path.Combine(_root, "Other.sln"), string.Empty);
        Assert.Throws<InvalidOperationException>(() => new ToolConfig().SolutionPath(_root));
        Assert.Equal(Path.Combine(_root, "Other.sln"), new ToolConfig { Solution = "Other.sln" }.SolutionPath(_root));
    }

    [Fact]
    public void SmellsAreFoundOnlyInTheConfiguredFoldersAndWithTheConfiguredLimits()
    {
        var body = string.Join('\n', Enumerable.Range(0, 12).Select(i => $"        System.Console.WriteLine({i});"));
        WriteSource("app/Long.cs", $"class A\n{{\n    void M()\n    {{\n{body}\n    }}\n}}\n");
        WriteSource("src/Ignored.cs", $"class B\n{{\n    void M()\n    {{\n{body}\n    }}\n}}\n");
        var config = new ToolConfig { SourceFolders = new[] { "app" }, Limits = new Thresholds { MethodLines = 10 } };

        var findings = Smells.Scan(_root, includeTests: false, config);

        var found = Assert.Single(findings, f => f.Rule == "LongMethod");
        Assert.Equal("app/Long.cs", found.File);
    }
}
