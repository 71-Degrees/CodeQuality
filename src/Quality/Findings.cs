/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.Json;

namespace CodeQuality.Quality;

/// <summary>One thing a check noticed. <see cref="Key"/> is stable across edits (rule + file + member, never a line number) so the baseline survives people changing the code around it.
/// <see cref="Value"/> is the measurement (a method's length, a file's warning count) and is what the ratchet compares.</summary>
internal sealed record Finding(string Rule, string File, string Member, int Line, int Value, string Message)
{
    public string Key => $"{Rule}|{File}|{Member}";
}

/// <summary>
/// The ratchet. <c>quality/baseline.json</c> (committed) records the findings that exist today and are accepted; a run reports only what is NEW or WORSE than that, and fails on it.
/// Clean-up shrinks the baseline (<c>--update-baseline</c> after fixing); new code cannot make anything worse without someone choosing to accept it. This is how quality stops drifting
/// without having to fix 20 years of history first.
/// </summary>
internal sealed class Baseline
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public Dictionary<string, int> Entries { get; set; } = new();

    public static string PathFor(string root) => Path.Combine(root, "quality", "baseline.json");

    public static Baseline Load(string root)
    {
        var path = PathFor(root);
        return File.Exists(path) ? JsonSerializer.Deserialize<Baseline>(File.ReadAllText(path)) ?? new Baseline() : new Baseline();
    }

    public void Save(string root)
    {
        var path = PathFor(root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sorted = new Baseline { Entries = Entries.OrderBy(e => e.Key, StringComparer.Ordinal).ToDictionary(e => e.Key, e => e.Value) };
        File.WriteAllText(path, JsonSerializer.Serialize(sorted, Json) + Environment.NewLine);
    }

    public static Baseline From(IEnumerable<Finding> findings)
    {
        var baseline = new Baseline();
        foreach (var finding in findings)
        {
            baseline.Entries[finding.Key] = Math.Max(finding.Value, baseline.Entries.GetValueOrDefault(finding.Key));
        }

        return baseline;
    }

    /// <summary>What the findings add up to against this baseline.</summary>
    public Comparison Compare(IReadOnlyList<Finding> findings, Func<string, bool>? inScope = null)
    {
        var now = findings.GroupBy(f => f.Key).ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.Value).First());
        var added = new List<Finding>();
        var worse = new List<(Finding Finding, int Was)>();
        foreach (var (key, finding) in now)
        {
            if (!Entries.TryGetValue(key, out var was))
            {
                added.Add(finding);
            }
            else if (finding.Value > was)
            {
                worse.Add((finding, was));
            }
        }

        var fixedKeys = Entries.Keys.Where(k => !now.ContainsKey(k) && (inScope?.Invoke(k) ?? true)).ToList();
        return new Comparison(added.OrderBy(f => f.File).ThenBy(f => f.Line).ToList(), worse.OrderBy(w => w.Finding.File).ToList(), fixedKeys, now.Count);
    }
}

internal sealed record Comparison(IReadOnlyList<Finding> New, IReadOnlyList<(Finding Finding, int Was)> Worse, IReadOnlyList<string> Fixed, int Total)
{
    public bool Clean => New.Count == 0 && Worse.Count == 0;
}
