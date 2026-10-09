/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

// Applies the fixes for .NET analyzer warnings that need no judgment, using Roslyn so each edit lands exactly where the compiler pointed. It never changes what the code does: it adds the culture a
// parse or format should have named, seals a class nothing inherits from, makes a class that owns a lock disposable, and so on (see Fixers/).
//
//   <refactor> fix-analyzers                  # DRY RUN: builds, finds the warnings, prints what it would change, changes nothing
//   <refactor> fix-analyzers --apply          # make the changes (needs a clean git tree, so every step is one reviewable commit)
//   <refactor> fix-analyzers --rules CA1816,CA1305   # only these rules
//   <refactor> fix-analyzers --target tools/Deploy/Deploy.csproj   # one project (default: the whole solution; tools/ are not in it)
//
//   <refactor> extract-block --file src/X/Program.cs --from 745 --to 815 --into src/X/Endpoints/OpsEndpoints.cs --class OpsEndpoints --method MapOps --receiver "WebApplication app" --params "..."
//       moves lines FROM..TO into a static extension method in another file and leaves one call behind (dry run unless --apply); the block is carried over unchanged, only indented.
//
//   <refactor> log-empty-catches [--apply]   # every empty catch in a type with a logger gets a debug line saying what it ignored (the owner's no-empty-catch rule)
//
// After --apply: build, run the tests, review the diff, commit. Warnings no fixer can fix safely are listed as "left for a person".

using CodeQuality.Common;
using CodeQuality.Refactor;
using CodeQuality.Refactor.Fixers;

internal static class Program
{
    private static readonly IReadOnlyList<IFixer> AllFixers = new IFixer[]
    {
        new SuppressFinalizeFixer(), new SealClassFixer(), new CultureFixer(), new StringComparisonFixer(), new DisposableOwnerFixer(),
    };

    private static async Task<int> Main(string[] args)
    {
        switch (args.FirstOrDefault())
        {
            case "fix-analyzers":
                return await FixAnalyzersAsync(args);
            case "extract-block":
                return await ExtractBlockCommand.RunAsync(args);
            case "log-empty-catches":
                return await LogEmptyCatchesCommand.RunAsync(args);
            case "lambdas-to-methods":
                return await LambdasToMethodsCommand.RunAsync(args);
            default:
                Console.Error.WriteLine($"Usage:{Environment.NewLine}  fix-analyzers [--apply] [--rules CA1816,CA1305] [--target PROJECT]   (dry run unless --apply){Environment.NewLine}  {ExtractBlockCommand.Usage}{Environment.NewLine}  {LambdasToMethodsCommand.Usage}{Environment.NewLine}  {LogEmptyCatchesCommand.Usage}");
                return 2;
        }
    }

    private static async Task<int> FixAnalyzersAsync(string[] args)
    {
        var apply = args.Contains("--apply");
        var root = Repo.FindRoot();
        if (apply && !await GitTree.IsCleanAsync(root))
        {
            Console.Error.WriteLine("The git working tree has uncommitted changes. Commit or stash them first, so this step is one commit that can be reviewed or reverted on its own.");
            return 2;
        }

        var fixers = Choose(args);
        Console.WriteLine("Building to collect the analyzer warnings (about a minute) ...");
        var warnings = (await BuildWarnings.CollectAsync(root, ValueOf(args, "--target") ?? ToolConfig.Load(root).SolutionPath(root)))
            .Where(w => fixers.Any(f => f.Rules.Contains(w.Rule)) || args.Contains("--list-all")).ToList();
        return Run(root, warnings, fixers, apply);
    }

    private static string? ValueOf(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static IReadOnlyList<IFixer> Choose(string[] args)
    {
        if (ValueOf(args, "--rules") is not { } rules)
        {
            return AllFixers;
        }

        var wanted = rules.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return AllFixers.Where(f => f.Rules.Any(wanted.Contains)).ToList();
    }

    private static int Run(string root, IReadOnlyList<Warning> warnings, IReadOnlyList<IFixer> fixers, bool apply)
    {
        var summary = new RunSummary();
        foreach (var file in warnings.GroupBy(w => w.File))
        {
            var original = File.ReadAllText(file.Key);
            var edit = FileEditor.Edit(original, file, fixers);
            summary.Add(Path.GetRelativePath(root, file.Key), edit);
            if (apply && edit.Fixed.Count > 0)
            {
                File.WriteAllText(file.Key, edit.Text);
            }
        }

        summary.Print(apply);
        return 0;
    }
}
