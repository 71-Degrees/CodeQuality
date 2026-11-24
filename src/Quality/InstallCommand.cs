/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.Json;
using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>
/// "quality install" publishes CodeQuality once, machine-wide, so every repo runs the same copy; "install --check" says whether that copy is behind origin.
/// Playwright's browser is not installed here: the Doctor checks the browser separately.
/// </summary>
internal static class InstallCommand
{
    public static async Task<int> RunAsync(Options options)
    {
        var home = QualityHome.From(options);
        return options.Flag("--check")
            ? await CheckAsync(home)
            : await InstallAsync(home, options.Flag("--no-pull"));
    }

    private static async Task<int> CheckAsync(QualityHome home)
    {
        await Shell.RunAsync("git", $"-C \"{home.Source}\" fetch --quiet origin", home.Source, 120);
        var origin = await GitOutputAsync(home.Source, "rev-parse origin/master");
        var installed = InstallState.Read(home.InstalledFile);
        int? behind = null;
        if (installed is not null)
        {
            var count = await GitOutputAsync(home.Source, $"rev-list --count {installed.Commit}..origin/master");
            behind = int.TryParse(count, out var n) ? n : null;
        }

        var (code, message) = FreshnessCheck.Compare(installed, origin, behind);
        Console.WriteLine(message);
        return code;
    }

    private static async Task<int> InstallAsync(QualityHome home, bool noPull)
    {
        if (home.Contains(AppContext.BaseDirectory))
        {
            Console.WriteLine("install runs from the CodeQuality checkout, not from the published copy (a running copy cannot replace itself): "
                + $"dotnet run --project \"{Path.Combine(home.Source, "src", "Quality")}\" -- install");
            return 2;
        }

        if (!noPull && !await PullAsync(home))
        {
            return 2;
        }

        if (!await PublishAsync(home))
        {
            return 2;
        }

        if (!SwapIn(home))
        {
            return 1;
        }

        if (!await InstallNodeToolsAsync(home))
        {
            return 2;
        }

        return await RecordAsync(home);
    }

    private static async Task<bool> PullAsync(QualityHome home)
    {
        var (code, output) = await Shell.RunAsync("git", $"-C \"{home.Source}\" pull --ff-only", home.Source, 300);
        if (code == 0)
        {
            return true;
        }

        Console.WriteLine(LastLine(output));
        return false;
    }

    private static async Task<bool> PublishAsync(QualityHome home)
    {
        return await PublishOneAsync(home, Path.Combine("src", "Quality", "Quality.csproj"), home.Bin + ".next")
            && await PublishOneAsync(home, Path.Combine("src", "Refactor", "Refactor.csproj"), home.Refactor + ".next");
    }

    private static async Task<bool> PublishOneAsync(QualityHome home, string project, string target)
    {
        if (Directory.Exists(target))
        {
            Directory.Delete(target, recursive: true);
        }

        var csproj = Path.Combine(home.Source, project);
        var (code, output) = await Shell.RunAsync("dotnet", $"publish \"{csproj}\" -c Release -o \"{target}\" --nologo", home.Source, 600);
        if (code == 0)
        {
            return true;
        }

        foreach (var line in output.Split('\n').Where(l => l.Contains(" error ")).Take(3))
        {
            Console.WriteLine(line.Trim());
        }

        return false;
    }

    private static bool SwapIn(QualityHome home) =>
        SwapOne(home.Bin + ".next", home.Bin) && SwapOne(home.Refactor + ".next", home.Refactor);

    private static bool SwapOne(string next, string live)
    {
        if (FolderSwap.TrySwap(next, live, out var problem))
        {
            return true;
        }

        Console.WriteLine(problem);
        return false;
    }

    private static async Task<bool> InstallNodeToolsAsync(QualityHome home)
    {
        NodePath.Ensure();
        Directory.CreateDirectory(home.Node);
        // The packages and the one shared lint rule set (quality scripts runs ESLint with it against every repo).
        foreach (var file in new[] { "package.json", "package-lock.json", "eslint.config.mjs" })
        {
            File.Copy(Path.Combine(home.Source, "node", file), Path.Combine(home.Node, file), overwrite: true);
        }

        if (!await NpmAsync(home.Node, "ci --no-audit --no-fund"))
        {
            return false;
        }

        // npm update takes minor and patch releases inside each caret range in package.json (the major-version floor the owner set),
        // and never a new major.
        return await NpmAsync(home.Node, "update --no-audit --no-fund");
    }

    private static async Task<bool> NpmAsync(string folder, string arguments)
    {
        var (code, output) = await Shell.RunAsync("cmd.exe", $"/c npm {arguments}", folder, 600);
        if (code == 0)
        {
            return true;
        }

        Console.WriteLine(LastLine(output));
        return false;
    }

    private static async Task<int> RecordAsync(QualityHome home)
    {
        var commit = await GitOutputAsync(home.Source, "rev-parse HEAD");
        if (commit is null)
        {
            Console.WriteLine("could not read CodeQuality's commit, so the install was not recorded");
            return 2;
        }

        var typeScript = VersionOf(home.Node, "typescript");
        var eslint = VersionOf(home.Node, "eslint");
        new InstallState(commit, DateTimeOffset.UtcNow, typeScript, eslint).Write(home.InstalledFile);
        Console.WriteLine($"Installed CodeQuality {FreshnessCheck.Short(commit)} to {home.Home}: TypeScript {typeScript ?? "?"}, ESLint {eslint ?? "?"}.");
        return 0;
    }

    private static string? VersionOf(string nodeFolder, string package)
    {
        var file = Path.Combine(nodeFolder, "node_modules", package, "package.json");
        if (!File.Exists(file))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(file));
        return document.RootElement.TryGetProperty("version", out var version) ? version.GetString() : null;
    }

    /// <summary>Trimmed output of a git call, or null when git fails.</summary>
    private static async Task<string?> GitOutputAsync(string source, string arguments)
    {
        var (code, output) = await Shell.RunAsync("git", $"-C \"{source}\" {arguments}", source, 60);
        return code == 0 ? output.Trim() : null;
    }

    private static string LastLine(string output) =>
        output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? string.Empty;
}
