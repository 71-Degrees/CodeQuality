/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Refactor;

/// <summary>The start of every rewriting command: refuse to write over uncommitted work, and choose the folders to walk (<c>--path</c> or the site's defaults).</summary>
internal static class CommandSetup
{
    /// <summary>True when the command may go ahead. A write over uncommitted changes is refused, with the reason, unless <c>--allow-dirty</c> was given.</summary>
    public static async Task<bool> TreeIsReadyAsync(CommandOptions options, string root)
    {
        if (!options.Flag("--apply") || options.Flag("--allow-dirty") || await GitTree.IsCleanAsync(root))
        {
            return true;
        }

        Console.Error.WriteLine("The git working tree has uncommitted changes. Commit them first (or pass --allow-dirty).");
        return false;
    }

    public static IReadOnlyList<string> Folders(CommandOptions options, string root, IEnumerable<string> defaults) =>
        options.Has("--path")
            ? new[] { Path.GetFullPath(options.Text("--path"), root) }
            : defaults.Select(folder => Path.Combine(root, folder)).ToArray();
}
