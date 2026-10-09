/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Globalization;
using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>A file that is both changed often and carries findings. <see cref="Score"/> is commits times pressure.</summary>
internal sealed record Hotspot(string File, int Commits, int LinesChanged, int Findings, double Pressure, double Score);

/// <summary>
/// Where refactoring pays: code that is hard to change AND is being changed. A giant file nobody touches costs little; a medium one edited every week costs a lot. The churn comes from git
/// (<c>git log --numstat</c>) over the last few weeks, the difficulty from the findings.
/// </summary>
internal static class Hotspots
{
    public static async Task<IReadOnlyList<Hotspot>> FindAsync(string root, int days, IEnumerable<Finding> findings, int top = 15)
    {
        var log = await Shell.RunAsync("git", $"log --since={days}.days --no-renames --numstat --format=@@%H", root, 60);
        return log.Code == 0 ? Rank(Parse(log.Output), findings, top) : Array.Empty<Hotspot>();
    }

    /// <summary>The number of commits and of changed lines per file, from <c>git log --numstat --format=@@%H</c> output.</summary>
    public static Dictionary<string, (int Commits, int Lines)> Parse(string output)
    {
        var churn = new Dictionary<string, (int Commits, int Lines)>(StringComparer.Ordinal);
        var inThisCommit = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                inThisCommit.Clear();
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length < 3)
            {
                continue;
            }

            var file = parts[2].Replace('\\', '/');
            var changed = Count(parts[0]) + Count(parts[1]);
            var (commits, lines) = churn.GetValueOrDefault(file);
            churn[file] = (commits + (inThisCommit.Add(file) ? 1 : 0), lines + changed);
        }

        return churn;
    }

    /// <summary>The files with findings, most worth refactoring first.</summary>
    public static IReadOnlyList<Hotspot> Rank(Dictionary<string, (int Commits, int Lines)> churn, IEnumerable<Finding> findings, int top)
    {
        var hot = new List<Hotspot>();
        foreach (var file in findings.GroupBy(f => f.File))
        {
            if (!churn.TryGetValue(file.Key, out var changes))
            {
                continue;
            }

            var pressure = Math.Round(file.Sum(f => Weights.Of(f.Rule, f.Value)), 1);
            hot.Add(new Hotspot(file.Key, changes.Commits, changes.Lines, file.Count(), pressure, Math.Round(changes.Commits * pressure, 1)));
        }

        return hot.OrderByDescending(h => h.Score).ThenBy(h => h.File, StringComparer.Ordinal).Take(top).ToList();
    }

    private static int Count(string text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;   // binary files show "-"
}
