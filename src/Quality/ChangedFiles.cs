/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>A source file that changed: its path relative to the repository (forward slashes) and whether it lives in a test folder.</summary>
internal sealed record ChangedSource(string Relative, bool IsTest);

/// <summary>Which source files the working tree changes, for <c>quality quick</c>: what git says differs from HEAD (staged, unstaged and new files), or from a given commit (<c>--since origin/master</c>).</summary>
internal static class ChangedFiles
{
    /// <summary>The files one commit changed (<c>--commit</c>): for judging how much a by-name selection would have run for that change alone.</summary>
    internal static async Task<IReadOnlyList<string>> OfCommitAsync(string root, string commit)
    {
        var diff = await Shell.RunAsync("git", $"diff --name-only --diff-filter=ACMR {Quoted(commit)}^ {Quoted(commit)}", root, 60);
        return diff.Output.Split('\n').Select(line => line.Trim().Replace('\\', '/')).Where(line => line.Length > 0).ToList();
    }

    /// <summary>Paths relative to the repository root, forward slashes. Deleted files are left out (there is nothing to check), renames count as the new name.</summary>
    internal static async Task<IReadOnlyList<string>> FindAsync(string root, string? since)
    {
        var diff = await Shell.RunAsync("git", $"diff --name-only --diff-filter=ACMR {Quoted(since ?? "HEAD")}", root, 60);
        var fresh = await Shell.RunAsync("git", "ls-files --others --exclude-standard", root, 60);
        return diff.Output.Split('\n').Concat(fresh.Output.Split('\n'))
            .Select(line => line.Trim().Replace('\\', '/'))
            .Where(line => line.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The browser scripts among them (JavaScript or TypeScript under a web root, not a vendored library or a build folder).</summary>
    internal static IReadOnlyList<string> BrowserScripts(IEnumerable<string> files) =>
        [.. files.Where(f => (f.EndsWith(".js", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
            && f.Contains("wwwroot/", StringComparison.OrdinalIgnoreCase) && !f.Contains("/lib/", StringComparison.OrdinalIgnoreCase) && !f.Contains("node_modules/", StringComparison.Ordinal)
            && !f.Contains("/bin/", StringComparison.Ordinal) && !f.Contains("/obj/", StringComparison.Ordinal) && !f.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase))];

    /// <summary>The C# and Razor files among them that the full scan would also look at: under a source or test folder and not in a skipped place.</summary>
    internal static IReadOnlyList<ChangedSource> Sources(IEnumerable<string> files, ToolConfig config)
    {
        var result = new List<ChangedSource>();
        foreach (var file in files)
        {
            var isSource = file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase);
            if (!isSource || Smells.IsSkipped(file, config))
            {
                continue;
            }

            if (InFolder(file, config.TestFolders))
            {
                result.Add(new ChangedSource(file, IsTest: true));
            }
            else if (InFolder(file, config.SourceFolders))
            {
                result.Add(new ChangedSource(file, IsTest: false));
            }
        }

        return result;
    }

    private static bool InFolder(string file, IEnumerable<string> folders) =>
        folders.Any(folder => file.StartsWith(folder.Trim('/', '\\') + "/", StringComparison.OrdinalIgnoreCase));

    // A ref comes from the command line; it goes to git as one quoted argument, never as shell text.
    private static string Quoted(string value) => "\"" + value.Replace("\"", string.Empty) + "\"";
}
