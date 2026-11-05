/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;

namespace CodeQuality.Refactor;

/// <summary>The C# files a refactor command may rewrite: under the given folders, minus build output and any folder the site's quality.json leaves out.</summary>
internal static class CSharpFiles
{
    public static IReadOnlyList<string> Under(string root, ToolConfig config, IEnumerable<string> folders) =>
        folders.Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Where(file => IsOurs(file, root, config))
            .ToList();

    /// <summary>Not build output, and not in a folder the repository's quality.json says to leave out (another repository checked out inside it, for example).</summary>
    private static bool IsOurs(string path, string root, ToolConfig config)
    {
        var relative = "/" + Path.GetRelativePath(root, path).Replace('\\', '/');
        return !relative.Contains("/obj/", StringComparison.Ordinal)
            && !relative.Contains("/bin/", StringComparison.Ordinal)
            && !config.Skipped.Any(skipped => relative.Contains(skipped, StringComparison.Ordinal));
    }
}
