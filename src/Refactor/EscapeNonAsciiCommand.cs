/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;

namespace CodeQuality.Refactor;

/// <summary>The <c>escape-non-ascii</c> command line: writes the non-ASCII characters of string and char literals as <c>\u</c> escapes (see <see cref="NonAsciiEscaper"/>). A dry run unless <c>--apply</c>.</summary>
internal static class EscapeNonAsciiCommand
{
    public const string Usage = "escape-non-ascii [--path DIR] [--apply] [--allow-dirty]   (default: the source and test folders; a dry run unless --apply)";

    /// <summary>How many of the places left alone are listed by name; the rest are counted.</summary>
    private const int LeftListLimit = 50;

    public static async Task<int> RunAsync(string[] args)
    {
        var options = new CommandOptions(args);
        var root = Repo.FindRoot();
        var apply = options.Flag("--apply");
        if (!await CommandSetup.TreeIsReadyAsync(options, root))
        {
            return 2;
        }

        var config = ToolConfig.Load(root);
        var folders = CommandSetup.Folders(options, root, config.SourceFolders.Concat(config.TestFolders));
        var escaped = 0;
        var left = new List<string>();
        foreach (var file in CSharpFiles.Under(root, config, folders))
        {
            var result = NonAsciiEscaper.ApplyToFile(file, apply);
            var relative = Path.GetRelativePath(root, file);
            escaped += result.Escaped;
            left.AddRange(result.Left.Select(place => $"{relative}:{place}"));
            if (result.Escaped > 0)
            {
                Console.WriteLine($"{(apply ? "Escaped" : "Would escape")} {result.Escaped} in {relative}");
            }
        }

        Console.WriteLine($"{escaped} literal(s) {(apply ? "escaped" : "would be escaped")}.");
        Console.WriteLine($"{left.Count} place(s) left alone (verbatim, raw or comment):");
        left.Take(LeftListLimit).ToList().ForEach(place => Console.WriteLine($"  {place}"));
        if (left.Count > LeftListLimit)
        {
            Console.WriteLine($"  ... and {left.Count - LeftListLimit} more");
        }

        return 0;
    }
}
