/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary><c>quality browser --generate</c>: writes the site's browser-test project from quality/features.json (see <see cref="BrowserGenerator"/>).</summary>
internal static class BrowserCommand
{
    internal static int Run(string root, Options options, ToolConfig config)
    {
        if (!options.Flag("--generate"))
        {
            Console.WriteLine("browser --generate: writes a browser-test project from quality/features.json (needs a `browser` block in quality/quality.json).");
            return 0;
        }

        if (config.Browser?.WebProject is not { Length: > 0 } webProject)
        {
            Console.WriteLine("quality/quality.json has no `browser.webProject` (the web .csproj to start, e.g. \"src/MyApp.Web/MyApp.Web.csproj\"). Add it, then run this again.");
            return 2;
        }

        if (FeatureRegistryFile.Load(root, config.Features) is not { } registry)
        {
            Console.WriteLine("No quality/features.json: run `quality features --generate` (or `quality init`) first.");
            return 2;
        }

        var siteName = config.SiteName ?? Path.GetFileName(root);
        var folder = Path.Combine(root, config.Browser.TestProject ?? Path.Combine("tests", BrowserGenerator.Identifier(siteName) + ".Browser.Tests"));
        var toolFolder = Path.Combine(root, "tools", "CodeQuality");
        var files = BrowserGenerator.Files(siteName, config.Browser, registry, Path.GetRelativePath(folder, toolFolder), webProject);
        Directory.CreateDirectory(folder);
        foreach (var (name, content) in files)
        {
            var path = Path.Combine(folder, name);
            var rewrite = name == "Journeys.cs";
            if (File.Exists(path) && !rewrite)
            {
                continue;
            }

            File.WriteAllText(path, content);
            Console.WriteLine($"  {(rewrite ? "wrote" : "created")} {Path.GetRelativePath(root, path)}");
        }

        Console.WriteLine($"{registry.Functions.Length} function(s) in the list. Run: dotnet test {Path.GetRelativePath(root, folder)}");
        return 0;
    }
}
