/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;

namespace CodeQuality.Refactor;

/// <summary>The <c>extract-block</c> command line: reads the options, plans the move, shows it, and (with <c>--apply</c>) writes both files.</summary>
internal static class ExtractBlockCommand
{
    public const string Usage =
        "extract-block --file F --from N --to M --into PATH --class NAME --method NAME --receiver \"WebApplication app\" --namespace NS [--params \"T a, T b\"] [--summary TEXT] [--call TEXT] [--apply] [--allow-dirty]";

    public static async Task<int> RunAsync(string[] args)
    {
        var options = new CommandOptions(args);
        if (options.Required("--file", "--from", "--to", "--into", "--class", "--method", "--receiver", "--namespace") is { } missing)
        {
            Console.Error.WriteLine($"Missing {missing}. Usage: {Usage}");
            return 2;
        }

        var root = Repo.FindRoot();
        var file = Path.GetFullPath(options.Text("--file"), root);
        var into = Path.GetFullPath(options.Text("--into"), root);
        if (options.Flag("--apply") && !options.Flag("--allow-dirty") && !await GitTree.IsCleanAsync(root))
        {
            Console.Error.WriteLine("The git working tree has uncommitted changes. Commit them first (or pass --allow-dirty).");
            return 2;
        }

        var request = new ExtractRequest(
            options.Int("--from"), options.Int("--to"), options.Text("--class"), options.Text("--method"), options.Text("--receiver"),
            options.Text("--params", string.Empty), options.Text("--namespace"), options.Text("--summary", string.Empty), options.Has("--call") ? options.Text("--call") : null);
        var plan = ExtractBlock.Plan(File.ReadAllText(file), File.Exists(into) ? File.ReadAllText(into) : null, request);
        return Report(options.Flag("--apply"), file, into, plan, root);
    }

    private static int Report(bool apply, string file, string into, ExtractPlan plan, string root)
    {
        Console.WriteLine($"{(apply ? "Moved" : "Would move")} {plan.MovedLines} lines from {Path.GetRelativePath(root, file)} into {Path.GetRelativePath(root, into)}.");
        if (!apply)
        {
            Console.WriteLine("Dry run: nothing was changed. Add --apply to make the change, then build: the compiler names any parameter the block still needs.");
            return 0;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(into)!);
        File.WriteAllText(into, plan.Target);
        File.WriteAllText(file, plan.Source);
        return 0;
    }
}
