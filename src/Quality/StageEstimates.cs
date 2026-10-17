/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.Json;

namespace CodeQuality.Quality;

/// <summary>
/// How long a step usually takes, from the steps of earlier runs: this tool's own history first, else the push check's <c>reports/check-timings.jsonl</c> (the same step names, so a full run is
/// estimated from the pushes before it). The median of the last few passing runs, so one slow day does not stretch every estimate.
/// </summary>
internal sealed class StageEstimates
{
    private const int Window = 5;

    internal const string OwnFile = "quality-run-timings.jsonl";

    private readonly Dictionary<string, List<double>> _own;
    private readonly Dictionary<string, List<double>> _push;

    internal StageEstimates(IEnumerable<string> ownLines, IEnumerable<string> pushLines)
    {
        _own = Read(ownLines);
        _push = Read(pushLines);
    }

    internal static StageEstimates None { get; } = new([], []);

    internal static StageEstimates Load(string reports) => new(Lines(Path.Combine(reports, OwnFile)), Lines(Path.Combine(reports, "check-timings.jsonl")));

    /// <summary>The usual seconds for this step, or null when no earlier run recorded it.</summary>
    internal double? For(string stage)
    {
        var seen = _own.TryGetValue(stage, out var own) ? own : _push.GetValueOrDefault(stage);
        if (seen is not { Count: > 0 })
        {
            return null;
        }

        var sorted = seen.OrderBy(s => s).ToList();
        return sorted[sorted.Count / 2];
    }

    private static IEnumerable<string> Lines(string path) => File.Exists(path) ? File.ReadLines(path) : [];

    private static Dictionary<string, List<double>> Read(IEnumerable<string> lines)
    {
        var byStage = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (TryParse(line) is not { } point)
            {
                continue;
            }

            var (stage, seconds) = point;
            if (!byStage.TryGetValue(stage, out var seen))
            {
                byStage[stage] = seen = [];
            }

            seen.Add(seconds);
            if (seen.Count > Window)
            {
                seen.RemoveAt(0);
            }
        }

        return byStage;
    }

    // A line that is not a passing timing (a stage that failed stops early and says nothing about the usual time) or not JSON at all is simply not a data point.
    private static (string Stage, double Seconds)? TryParse(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var row = doc.RootElement;
            var passed = !row.TryGetProperty("passed", out var p) || p.ValueKind != JsonValueKind.False;
            if (!passed || !row.TryGetProperty("stage", out var stage) || !row.TryGetProperty("seconds", out var seconds) || stage.GetString() is not { Length: > 0 } name)
            {
                return null;
            }

            return (name, seconds.GetDouble());
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
