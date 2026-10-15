/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text;
using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>
/// <c>quality setup</c>: the first-run checklist. It lists what this site has not yet done with Code Quality, best value for the time first, with what each costs to run and to set up and which
/// need the owner's OK; <c>--skip &lt;id&gt; --reason "..."</c> records a deliberate no, <c>--acknowledge</c> records that the first run was reviewed. Every other run ends with
/// <see cref="Notice"/>: the full "FIRST RUN" list until the site has been reviewed (no <c>quality/setup.json</c> yet), then a one-line reminder while anything is outstanding. It never changes an exit code.
/// </summary>
internal static class SetupCommand
{
    internal static int Run(string root, Options options, ToolConfig config)
    {
        var file = SetupFile.Load(root);
        if (options.Text("--skip", string.Empty) is { Length: > 0 } skip)
        {
            return Skip(root, file, config, skip, options.Text("--reason", string.Empty));
        }

        if (options.Flag("--acknowledge"))
        {
            file.FirstRun ??= DateTime.Now.ToString("yyyy-MM-dd");
            file.Save(root);
            Console.WriteLine($"Recorded: the first run was reviewed on {file.FirstRun}. quality/setup.json is committed with the baseline.");
        }

        Console.WriteLine(Render(Statuses(root, config, file), config, firstRun: !SetupFile.Exists(root) && !options.Flag("--acknowledge")));
        return 0;
    }

    /// <summary>What every other run appends: the first-run list when the site was never reviewed, a one-line reminder while items are outstanding, nothing when there are none.</summary>
    internal static string Notice(string root, ToolConfig config, Collected collected)
    {
        var file = SetupFile.Load(root);
        var statuses = SetupChecklist.Evaluate(new SetupFacts(root, config, collected, TestResults.Read(root, config), file));
        var outstanding = SetupChecklist.Outstanding(statuses);
        if (outstanding.Count == 0)
        {
            return string.Empty;
        }

        return SetupFile.Exists(root)
            ? $"Setup: {outstanding.Count} first-run item(s) still outstanding ({string.Join(", ", outstanding.Select(s => s.Item.Id))}) - `{config.ToolCommand.TrimEnd()} setup` lists them."
            : Render(statuses, config, firstRun: true);
    }

    private static IReadOnlyList<SetupStatus> Statuses(string root, ToolConfig config, SetupFile file) =>
        SetupChecklist.Evaluate(new SetupFacts(root, config, Collected.Load(root), TestResults.Read(root, config), file));

    private static int Skip(string root, SetupFile file, ToolConfig config, string id, string reason)
    {
        var known = SetupChecklist.Evaluate(new SetupFacts(root, config, new Collected(), null, file)).Select(s => s.Item.Id).ToList();
        if (!known.Contains(id) || string.IsNullOrWhiteSpace(reason))
        {
            Console.Error.WriteLine(!known.Contains(id) ? $"Unknown item '{id}'. Known: {string.Join(", ", known)}." : "Say why: --skip <id> --reason \"...\" (the reason is committed with the decision).");
            return 2;
        }

        file.FirstRun ??= DateTime.Now.ToString("yyyy-MM-dd");
        file.Skipped[id] = new SkipNote(reason.Trim(), DateTime.Now.ToString("yyyy-MM-dd"));
        file.Save(root);
        Console.WriteLine($"Recorded: {id} skipped ({reason.Trim()}). Commit quality/setup.json.");
        return 0;
    }

    internal static string Render(IReadOnlyList<SetupStatus> statuses, ToolConfig config, bool firstRun)
    {
        var outstanding = SetupChecklist.Outstanding(statuses);
        var text = new StringBuilder();
        if (firstRun)
        {
            text.AppendLine("FIRST RUN - this site has not been through Code Quality's first-time setup (no quality/setup.json).");
        }

        text.AppendLine(outstanding.Count == 0
            ? $"Setup: all {statuses.Count} first-run items are done, skipped on purpose or not applicable."
            : $"Setup: {outstanding.Count} of {statuses.Count} first-run items are outstanding, best value for the time first:");
        var number = 0;
        foreach (var s in outstanding)
        {
            var item = s.Item;
            text.AppendLine($"  {++number,2}. {item.Title}   [{item.Id}]");
            text.AppendLine($"      does:    {item.What}");
            text.AppendLine($"      value {Stars(item.Benefit)}   run {Minutes(item.RunMinutes)}   set up {Minutes(item.EffortMinutes)}{(item.NeedsApproval ? "   NEEDS THE OWNER'S OK" : string.Empty)}");
            text.AppendLine($"      runs as: {item.Cost.Engine}");
            text.AppendLine($"      tokens:  {Label(item.Cost.Tokens)} - {item.Cost.TokensWhy}");
            text.AppendLine($"      machine: {Label(item.Cost.Machine)} - {item.Cost.MachineWhy}");
            text.AppendLine($"      why:     {s.Note}");
            text.AppendLine($"      how:     {item.How.Replace("{tool}", config.ToolCommand.TrimEnd(), StringComparison.Ordinal)}");
        }

        foreach (var s in statuses.Where(s => s.State is SetupState.Skipped or SetupState.NotApplicable))
        {
            text.AppendLine($"  -  {s.Item.Title}: {s.Note}");
        }

        if (outstanding.Count > 0)
        {
            text.AppendLine("Claude: tell the user which of these are missing (this ranked list), ask which to do now, and do not run an item marked NEEDS THE OWNER'S OK without their approval.");
            text.AppendLine($"        A deliberate no is `{config.ToolCommand.TrimEnd()} setup --skip <id> --reason \"...\"`; `--acknowledge` records that the list was reviewed. Commit quality/setup.json.");
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>"none", "LOW", "MEDIUM", "HIGH": capitals for anything that costs something, so a glance finds the expensive ones.</summary>
    private static string Label(Level level) => level == Level.None ? "none" : level.ToString().ToUpperInvariant();

    private static string Stars(int benefit) => new string('*', benefit).PadRight(5, '.');

    private static string Minutes(int minutes) => minutes == 0 ? "-" : minutes >= 60 ? $"{minutes / 60.0:0.#} h" : $"{minutes} min";
}
