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
