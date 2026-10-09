/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;

namespace CodeQuality.Refactor;

/// <summary>The <c>log-empty-catches</c> command line: gives every empty catch in a type that has a logger a debug line saying what it ignored (see <see cref="EmptyCatchLogger"/>).</summary>
internal static class LogEmptyCatchesCommand
{
    public const string Usage = "log-empty-catches [--path DIR] [--apply] [--allow-dirty]   (default path: the whole repository's src/ and tools/)";

    public static async Task<int> RunAsync(string[] args)
    {
        var options = new CommandOptions(args);
        var root = Repo.FindRoot();
        if (options.Flag("--apply") && !options.Flag("--allow-dirty") && !await GitTree.IsCleanAsync(root))
        {
            Console.Error.WriteLine("The git working tree has uncommitted changes. Commit them first (or pass --allow-dirty).");
            return 2;
        }

        var config = ToolConfig.Load(root);
        var folders = options.Has("--path") ? new[] { Path.GetFullPath(options.Text("--path"), root) } : config.SourceFolders.Select(folder => Path.Combine(root, folder)).Where(Directory.Exists).ToArray();
        var totalLogged = 0;
        var left = new List<string>();
        foreach (var file in folders.SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)).Where(file => IsOurs(file, root, config)))
        {
            var result = EmptyCatchLogger.Apply(File.ReadAllText(file));
            totalLogged += result.Logged.Count;
            left.AddRange(result.Skipped.Select(s => $"{Path.GetRelativePath(root, file)}: {s}"));
            if (result.Logged.Count > 0)
            {
                Console.WriteLine($"{(options.Flag("--apply") ? "Logged" : "Would log")} {result.Logged.Count} in {Path.GetRelativePath(root, file)}: {string.Join(", ", result.Logged)}");
                if (options.Flag("--apply"))
                {
                    File.WriteAllText(file, result.Text);
                }
            }
        }

        Console.WriteLine($"{totalLogged} empty catch(es) {(options.Flag("--apply") ? "now log" : "would log")}; {left.Count} left for a person (no logger in reach):");
        left.ForEach(entry => Console.WriteLine($"  {entry}"));
        return 0;
    }

    /// <summary>Not build output, and not in a folder the repository's quality.json says to leave out (another repository checked out inside it, for example).</summary>
    private static bool IsOurs(string path, string root, ToolConfig config)
    {
        var relative = "/" + Path.GetRelativePath(root, path).Replace('\\', '/');
        return !relative.Contains("/obj/", StringComparison.Ordinal)
            && !relative.Contains("/bin/", StringComparison.Ordinal)
            && !config.SkipFolders.Any(skipped => relative.Contains(skipped, StringComparison.Ordinal));
    }
}
