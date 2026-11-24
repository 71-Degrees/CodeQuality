/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Quality;

namespace CodeQuality.Quality.Tests;

public sealed class ScriptToolsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "scripttools-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Write(string relative, string text = "")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void FindFolders_TakesOnlyProjectsWithScriptsOfTheirOwn_AndTheirTsConfig()
    {
        Write(@"src\App.Web\wwwroot\js\app.js");
        var tsconfig = Write(@"src\App.Web\tsconfig.json", "{}");
        Write(@"src\Other.Web\wwwroot\js\lib\vendor.js");
        Write(@"src\App.Core\Thing.cs");
        Write(@"src\Plain.Web\wwwroot\js\plain.js");

        var folders = ScriptTools.FindFolders(_root);

        Assert.Equal(["App.Web", "Plain.Web"], folders.Select(f => Path.GetFileName(f.Project)));
        Assert.Equal(tsconfig, folders[0].TsConfig);
        Assert.Null(folders[1].TsConfig);
    }

    [Fact]
    public void FindFolders_NoSrcFolder_IsEmpty() => Assert.Empty(ScriptTools.FindFolders(_root));

    [Fact]
    public void TscCommand_UsesTheProjectTsConfig_ElseTheDefaultSettings()
    {
        var b = Write(@"src\A\wwwroot\js\b.js");
        var a = Write(@"src\A\wwwroot\js\a.js");
        var scripts = Path.GetDirectoryName(a)!;
        var withConfig = new ScriptFolder(@"C:\r\src\A", scripts, @"C:\r\src\A\tsconfig.json");
        var without = withConfig with { TsConfig = null };

        Assert.Contains(@"-p ""C:\r\src\A\tsconfig.json""", ScriptTools.TscCommand(@"C:\q\node", withConfig));
        Assert.Contains(ScriptTools.DefaultTscOptions + $" \"{a}\" \"{b}\"", ScriptTools.TscCommand(@"C:\q\node", without));
        Assert.Contains(@"C:\q\node\node_modules\.bin\tsc.cmd", ScriptTools.TscCommand(@"C:\q\node", without));
    }

    [Fact]
    public void EslintCommand_SharedConfig_RelativeFolder_GlobalsAsFlags()
    {
        var folder = new ScriptFolder(@"C:\r\src\A", @"C:\r\src\A\wwwroot\js", null);
        var globals = new Dictionary<string, string> { ["L"] = "readonly", ["app"] = "writable" };

        var command = ScriptTools.EslintCommand(@"C:\q\node", @"C:\r", folder, globals);

        Assert.Contains(@"--config ""C:\q\node\eslint.config.mjs""", command);
        Assert.Contains(" --global L --global app:true ", command);
        Assert.EndsWith(@"""src\A\wwwroot\js""""", command);
    }

    [Fact]
    public void Locate_PrefersTheCopyBesideTheRunningTool_ThenTheFallback_ElseNull()
    {
        var beside = Path.Combine(_root, "home", "node");
        var fallback = Path.Combine(_root, "fallback", "node");
        var bin = Path.Combine(_root, "home", "bin");
        Directory.CreateDirectory(bin);

        Assert.Null(ScriptTools.Locate(bin, fallback));

        Write(Path.Combine("fallback", "node", "node_modules", ".bin", "tsc.cmd"));
        Write(Path.Combine("fallback", "node", "node_modules", ".bin", "eslint.cmd"));
        Assert.Equal(fallback, ScriptTools.Locate(bin, fallback));

        Write(Path.Combine("home", "node", "node_modules", ".bin", "tsc.cmd"));
        Write(Path.Combine("home", "node", "node_modules", ".bin", "eslint.cmd"));
        Assert.Equal(beside, ScriptTools.Locate(bin, fallback));
    }
}
