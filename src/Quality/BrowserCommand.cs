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
            Console.WriteLine("browser --generate: writes a browser-test project from quality/features.json (the `browser` block of quality/quality.json says how to start and sign in; the web project is found if it is not given).");
            return 0;
        }

        var (ok, message) = Generate(root, config);
        Console.WriteLine(message);
        return ok ? 0 : 2;
    }

    /// <summary>Writes the project and says what it did. <c>Journeys.cs</c> is rewritten each time; the other files are written once.</summary>
    internal static (bool Ok, string Message) Generate(string root, ToolConfig config)
    {
        var settings = config.Browser ?? new BrowserSettings();
        if ((settings.WebProject ?? FindWebProject(root, config)) is not { Length: > 0 } webProject)
        {
            return (false, "No web project found (a .csproj using Microsoft.NET.Sdk.Web under the source folders). Say which in quality/quality.json: browser.webProject.");
        }

        if (FeatureRegistryFile.Load(root, config.Features) is not { } registry)
        {
            return (false, "No quality/features.json: run `quality features --generate` (or `quality init`) first.");
        }

        var siteName = config.SiteName ?? Path.GetFileName(root);
        var folder = Path.Combine(root, settings.TestProject ?? Path.Combine("tests", BrowserGenerator.Identifier(siteName) + ".Browser.Tests"));
        var files = BrowserGenerator.Files(siteName, settings, registry, Path.GetRelativePath(folder, Path.Combine(root, "tools", "CodeQuality")), webProject);
        Directory.CreateDirectory(folder);
        var wrote = new List<string>();
        foreach (var (name, content) in files)
        {
            var path = Path.Combine(folder, name);
            if (File.Exists(path) && name != "Journeys.cs")
            {
                continue;
            }

            File.WriteAllText(path, content);
            wrote.Add(name);
        }

        return (true, $"wrote {string.Join(", ", wrote)} in {Path.GetRelativePath(root, folder)} for {registry.Functions.Length} function(s). Run: dotnet test {Path.GetRelativePath(root, folder)}");
    }

    /// <summary>The web project, when quality.json does not name it: the first project under the source folders that uses the web SDK, as a path relative to the root.</summary>
    internal static string? FindWebProject(string root, ToolConfig config) =>
        config.SourceFolders.Select(f => Path.Combine(root, f)).Where(Directory.Exists)
            .SelectMany(f => Directory.EnumerateFiles(f, "*.csproj", SearchOption.AllDirectories))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}tools{Path.DirectorySeparatorChar}CodeQuality{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault(p => File.ReadAllText(p).Contains("Microsoft.NET.Sdk.Web", StringComparison.Ordinal)) is { } found
            ? Path.GetRelativePath(root, found).Replace('\\', '/')
            : null;
}
