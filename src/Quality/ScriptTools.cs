/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Quality;

/// <summary>A repo's browser-script folder (<c>src/&lt;project&gt;/wwwroot/js</c>) and the tsconfig beside it, when the project has one.</summary>
internal sealed record ScriptFolder(string Project, string Scripts, string? TsConfig);

/// <summary>
/// The machine-wide script tools (TypeScript, ESLint, axe-core) that CodeQuality installs into its published copy (<c>quality install</c>,
/// the <c>node</c> folder beside <c>bin</c>), and how they are called against any repo, so no repo needs its own package.json or
/// node_modules (the owner, 2026-11-24). A repo's script folders are found, not configured: one that gains browser scripts is checked
/// with no setup.
/// </summary>
internal static class ScriptTools
{
    /// <summary>The compiler settings a project without its own tsconfig.json is checked with: the ones CrateDigger's tsconfig set. <c>--strict false</c>
    /// is spelled out because TypeScript 7 turns strict on by default, and plain browser scripts carry no type annotations to satisfy it.</summary>
    internal const string DefaultTscOptions = "--allowJs --checkJs --noEmit --strict false --target ES2022 --lib ES2022,DOM,DOM.Iterable --skipLibCheck";

    /// <summary>The installed node folder: beside the running copy (published), else the default published home (a run from source). Null
    /// when neither has TypeScript and ESLint installed.</summary>
    public static string? Locate() => Locate(AppContext.BaseDirectory, Path.Combine(QualityHome.DefaultHome, "node"));

    internal static string? Locate(string baseDirectory, string fallback) =>
        new[] { Path.GetFullPath(Path.Combine(baseDirectory, "..", "node")), fallback }.FirstOrDefault(IsInstalled);

    private static bool IsInstalled(string node) =>
        File.Exists(Path.Combine(node, "node_modules", ".bin", "tsc.cmd")) && File.Exists(Path.Combine(node, "node_modules", ".bin", "eslint.cmd"));

    /// <summary>Every <c>src/&lt;project&gt;/wwwroot/js</c> that holds at least one script of our own (vendored <c>lib</c> folders do not count).</summary>
    public static IReadOnlyList<ScriptFolder> FindFolders(string root)
    {
        var src = Path.Combine(root, "src");
        if (!Directory.Exists(src))
        {
            return [];
        }

        return Directory.GetDirectories(src)
            .Select(project => (Project: project, Scripts: Path.Combine(project, "wwwroot", "js")))
            .Where(p => Directory.Exists(p.Scripts) && Directory.EnumerateFiles(p.Scripts, "*.js", SearchOption.TopDirectoryOnly).Any())
            .Select(p => new ScriptFolder(p.Project, p.Scripts, File.Exists(Path.Combine(p.Project, "tsconfig.json")) ? Path.Combine(p.Project, "tsconfig.json") : null))
            .OrderBy(f => f.Project, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The type check for one folder: the project's own tsconfig when it has one (it lists the type files), else the default settings over its scripts.</summary>
    public static string TscCommand(string node, ScriptFolder folder) =>
        $"/c \"\"{Bin(node, "tsc")}\" " + (folder.TsConfig is { } tsconfig ? $"-p \"{tsconfig}\"" : $"{DefaultTscOptions} {ScriptList(folder.Scripts)}") + "\"";

    // tsc does not expand wildcards on Windows, so the scripts are named one by one.
    private static string ScriptList(string scripts) =>
        string.Join(' ', Directory.Exists(scripts)
            ? Directory.EnumerateFiles(scripts, "*.js", SearchOption.TopDirectoryOnly).Order(StringComparer.OrdinalIgnoreCase).Select(f => $"\"{f}\"")
            : []);

    /// <summary>The lint for one folder with the shared rule set, run from the repo root (ESLint matches a <c>--config</c> file's patterns against the
    /// working folder) and with the repo's page globals as <c>--global</c> flags: <c>writable</c> becomes <c>name:true</c>.</summary>
    public static string EslintCommand(string node, string root, ScriptFolder folder, IReadOnlyDictionary<string, string> globals)
    {
        var flags = string.Concat(globals.OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $" --global {g.Key}{(string.Equals(g.Value, "writable", StringComparison.OrdinalIgnoreCase) ? ":true" : string.Empty)}"));
        return $"/c \"\"{Bin(node, "eslint")}\" --config \"{Path.Combine(node, "eslint.config.mjs")}\" --no-warn-ignored{flags} \"{Path.GetRelativePath(root, folder.Scripts)}\"\"";
    }

    /// <summary>axe-core, the crawler's accessibility engine, from the installed tools.</summary>
    public static string? AxePath() => Locate() is { } node ? Path.Combine(node, "node_modules", "axe-core", "axe.min.js") : null;

    private static string Bin(string node, string tool) => Path.Combine(node, "node_modules", ".bin", tool + ".cmd");
}
