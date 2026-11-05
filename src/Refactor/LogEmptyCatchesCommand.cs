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
        if (!await CommandSetup.TreeIsReadyAsync(options, root))
        {
            return 2;
        }

        var config = ToolConfig.Load(root);
        var folders = CommandSetup.Folders(options, root, config.SourceFolders);
        var totalLogged = 0;
        var left = new List<string>();
        foreach (var file in CSharpFiles.Under(root, config, folders))
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
}
