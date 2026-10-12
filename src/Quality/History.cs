/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Globalization;
using System.Text.Json;
using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>How far over its limit a finding is, so "a 150-line method" weighs more than "a 61-line one". The sum over all smells is the single "refactoring pressure" number the history tracks.</summary>
internal static class Weights
{
    public static readonly IReadOnlyDictionary<string, double> Limits = new Dictionary<string, double>
    {
        ["LongMethod"] = 60, ["DuplicateCode"] = 10, ["Complexity"] = 15, ["DeepNesting"] = 4, ["ManyParameters"] = 7, ["LongFile"] = 700, ["RazorCodeBlock"] = 400,
    };

    public static double Of(string rule, int value) =>
        Limits.TryGetValue(rule, out var limit) ? Math.Max(1, value / limit) : rule.StartsWith("build/", StringComparison.Ordinal) ? 0.3 * value : 1;
}

/// <summary>What one check found at one moment: how many findings, how much pressure they add up to, and the count and pressure per rule.</summary>
internal sealed record CheckCounts(int Total, double Pressure, Dictionary<string, int> ByRule, Dictionary<string, double>? PressureByRule = null)
{
    public static CheckCounts From(IEnumerable<Finding> findings) => Of(findings.Select(f => (f.Rule, Weights.Of(f.Rule, f.Value))));

    /// <summary>The same numbers from baseline entries (key = rule|file|member, value = measurement), for snapshots rebuilt from git history.</summary>
    public static CheckCounts FromEntries(IEnumerable<KeyValuePair<string, int>> entries) => Of(entries.Select(e => (Rule: e.Key.Split('|')[0], Weights.Of(e.Key.Split('|')[0], e.Value))));

    public bool SameAs(CheckCounts? other) =>
        other is not null && Total == other.Total && Math.Abs(Pressure - other.Pressure) < 0.05 && ByRule.Count == other.ByRule.Count && ByRule.All(r => other.ByRule.GetValueOrDefault(r.Key) == r.Value);

    /// <summary>The count and pressure leaving out some rules (the ones added after the first recording), so a new check does not make the trend look worse.</summary>
    public (int Total, double Pressure) Without(IReadOnlyCollection<string> rules)
    {
        var total = ByRule.Where(r => !rules.Contains(r.Key)).Sum(r => r.Value);
        var pressure = PressureByRule is null ? Pressure : PressureByRule.Where(r => !rules.Contains(r.Key)).Sum(r => r.Value);
        return (total, Math.Round(pressure, 1));
    }

    private static CheckCounts Of(IEnumerable<(string Rule, double Weight)> items)
    {
        var list = items.ToList();
        var byRule = list.GroupBy(i => i.Rule).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        return new CheckCounts(list.Count, Math.Round(list.Sum(i => i.Weight), 1), byRule.ToDictionary(g => g.Key, g => g.Count()), byRule.ToDictionary(g => g.Key, g => Math.Round(g.Sum(i => i.Weight), 1)));
    }
}

/// <summary>The test numbers at one moment: how many tests, how many failed, and the line coverage (null when it was not collected).</summary>
internal sealed record TestCounts(int Total, int Failed, double? Coverage)
{
    public bool SameAs(TestCounts? other) => other is not null && Total == other.Total && Failed == other.Failed && Math.Abs((Coverage ?? -1) - (other.Coverage ?? -1)) < 0.0005;

    public static TestCounts? From(TestSummary? tests) => tests is null ? null : new TestCounts(tests.Total, tests.Failed, tests.CoverageRate is { } rate ? Math.Round(rate, 4) : null);
}

/// <summary>One point on the quality timeline. A check that was not measured at that time is null (the analyzers did not exist yet, for example).</summary>
internal sealed record Snapshot(string At, string Commit, string Note, CheckCounts? Smells, CheckCounts? Analyzers, TestCounts? Tests = null);

/// <summary>
/// The quality timeline, <c>quality/history.json</c> (committed): a snapshot each time a run's numbers differ from the last one, so the report can show where quality started, where it was last time and where it is now.
/// <c>history --backfill</c> rebuilds the early part from the git history of the baseline file.
/// </summary>
internal sealed class History
{
    public List<Snapshot> Snapshots { get; set; } = new();

    private static string PathFor(string root) => Path.Combine(root, "quality", "history.json");

    public static History Load(string root) => JsonState.Load<History>(PathFor(root));

    public void Save(string root) => JsonState.Save(PathFor(root), this);

    /// <summary>Adds a snapshot for this run when its numbers changed. A check that did not run this time keeps its numbers from the previous snapshot. Returns true when one was added.</summary>
    public bool Record(Collected collected, string commit, DateTime now)
    {
        var last = Snapshots.LastOrDefault();
        var smells = collected.RanThisRun.Contains("smells") ? CheckCounts.From(collected.Smells) : last?.Smells;
        var analyzers = collected.RanThisRun.Contains("analyzers") ? CheckCounts.From(collected.Analyzers) : last?.Analyzers;
        // The test numbers are whatever the test projects last wrote (see TestResults), so a snapshot also carries the tests as they were when the code smells were counted.
        var tests = TestCounts.From(collected.Tests) ?? last?.Tests;
        if (smells is null && analyzers is null && tests is null)
        {
            return false;
        }

        if (last is not null && SameNumbers(last.Smells, smells) && SameNumbers(last.Analyzers, analyzers) && (last.Tests is null ? tests is null : last.Tests.SameAs(tests)))
        {
            return false;
        }

        Snapshots.Add(new Snapshot(now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), commit, string.Empty, smells, analyzers, tests));
        return true;
    }

    private static bool SameNumbers(CheckCounts? a, CheckCounts? b) => a is null ? b is null : a.SameAs(b);

    /// <summary>Rebuilds the timeline from the commits that changed the baseline file (smells from every version; analyzers only from the versions that already recorded them).</summary>
    public static async Task<History> BackfillAsync(string root)
    {
        var log = await Shell.RunAsync("git", "log --reverse --format=%h|%cI|%s -- quality/baseline.json", root, 60);
        var history = new History();
        var analyzersRecorded = false;
        foreach (var line in log.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('|', 3);
            if (parts.Length < 3)
            {
                continue;
            }

            var shown = await Shell.RunAsync("git", $"show {parts[0]}:quality/baseline.json", root, 60);
            if (shown.Code != 0 || !TryRead(shown.Output, out var entries))
            {
                continue;
            }

            var smells = CheckCounts.FromEntries(entries.Where(e => !e.Key.StartsWith("build/", StringComparison.Ordinal)));
            var analyzerEntries = entries.Where(e => e.Key.StartsWith("build/", StringComparison.Ordinal)).ToList();
            // Once the analyzers have been recorded, a baseline with none of their keys means they found nothing (not that they were not run).
            analyzersRecorded |= analyzerEntries.Count > 0;
            var analyzers = analyzersRecorded ? CheckCounts.FromEntries(analyzerEntries) : null;
            var at = DateTimeOffset.Parse(parts[1], CultureInfo.InvariantCulture).ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            var next = new Snapshot(at, parts[0], parts[2].Length > 90 ? parts[2][..90] : parts[2], smells, analyzers);
            var last = history.Snapshots.LastOrDefault();
            if (last is null || !SameNumbers(last.Smells, smells) || !SameNumbers(last.Analyzers, analyzers))
            {
                history.Snapshots.Add(next);
            }
        }

        return history;
    }

    private static bool TryRead(string json, out Dictionary<string, int> entries)
    {
        try
        {
            entries = JsonSerializer.Deserialize<Baseline>(json)?.Entries ?? new();
            return true;
        }
        catch (JsonException)
        {
            entries = new();
            return false;
        }
    }

    /// <summary>One line for the console: where quality started and where it is now.</summary>
    public string? Summary()
    {
        if (Snapshots.Count < 2 || Snapshots[0].Smells is not { } first || Snapshots[^1].Smells is not { } now)
        {
            return null;
        }

        var (startTotal, startPressure) = first.Without(Rules.Newer);
        var (nowTotal, nowPressure) = now.Without(Rules.Newer);
        var text = $"Since the first recording ({Snapshots[0].At}): code smells {startTotal} -> {nowTotal}, refactoring pressure {startPressure:0.#} -> {nowPressure:0.#}";
        text = Snapshots[0].Analyzers is { } a && Snapshots[^1].Analyzers is { } b ? text + $", analyzer findings {a.Total} -> {b.Total}" : text;
        var newer = now.Total - nowTotal;
        return newer > 0 ? text + $" (not counted: {newer} finding(s) from checks added since)." : text + ".";
    }
}
