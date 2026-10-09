/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;

namespace CodeQuality.Refactor;

/// <summary>The <c>lambdas-to-methods</c> command line: plans the conversion, prints each handler and what it still needs, and (with <c>--apply</c>) rewrites the file.</summary>
internal static class LambdasToMethodsCommand
{
    public const string Usage = "lambdas-to-methods --file F [--instance] [--apply] [--allow-dirty]   (static handler methods unless --instance)";

    public static async Task<int> RunAsync(string[] args)
    {
        var options = new CommandOptions(args);
        if (options.Required("--file") is { } missing)
        {
            Console.Error.WriteLine($"Missing {missing}. Usage: {Usage}");
            return 2;
        }

        var root = Repo.FindRoot();
        var file = Path.GetFullPath(options.Text("--file"), root);
        if (options.Flag("--apply") && !options.Flag("--allow-dirty") && !await GitTree.IsCleanAsync(root))
        {
            Console.Error.WriteLine("The git working tree has uncommitted changes. Commit them first (or pass --allow-dirty).");
            return 2;
        }

        var conversion = LambdaHandlers.Convert(File.ReadAllText(file), options.Flag("--instance"));
        Print(Path.GetRelativePath(root, file), conversion, options.Flag("--apply"));
        if (options.Flag("--apply") && conversion.Converted.Count > 0)
        {
            File.WriteAllText(file, conversion.Text);
        }

        return 0;
    }

    private static void Print(string file, HandlerConversion conversion, bool apply)
    {
        Console.WriteLine($"{(apply ? "Converted" : "Would convert")} {conversion.Converted.Count} handler(s) in {file}:");
        foreach (var handler in conversion.Converted)
        {
            var captures = handler.Captures.Count == 0 ? string.Empty : $"   NEEDS: {string.Join(", ", handler.Captures)}";
            Console.WriteLine($"  {handler.Name,-48} <- {handler.Route}{captures}");
        }

        foreach (var skipped in conversion.Skipped)
        {
            Console.WriteLine($"  left as a lambda: {skipped}");
        }

        Console.WriteLine(apply ? "Next: build; each NEEDS name above is a variable of the registering method that the handler still uses." : "Dry run: nothing was changed. Add --apply to rewrite the file.");
    }
}
