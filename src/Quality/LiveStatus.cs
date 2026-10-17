/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeQuality.Quality;

internal enum StageState
{
    Pending,
    Running,
    Passed,
    Failed,
    Error,
    Skipped,
}

internal enum RunState
{
    Running,
    Passed,
    Failed,
    Error,
}

/// <summary>One step of a run as the live page shows it. The test counts are only set for a test project.</summary>
internal sealed record LiveStage(string Id, string Label, StageState State, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, double? EstimateSeconds, string Detail, int? Passed, int? Failed, int? Total);

/// <summary>A finding the baseline does not accept (<see cref="Kind"/> is "new" or "worse"), shown as soon as the step that found it ends.</summary>
internal sealed record LiveFinding(string Stage, string Kind, string Rule, string File, int Line, string Message);

/// <summary>Everything the live page draws, written whole each time (the page keeps no state of its own).</summary>
internal sealed record LiveSnapshot(string RunId, string Command, RunState State, string Verdict, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, DateTimeOffset UpdatedAt, DateTimeOffset? EstimatedFinishAt, IReadOnlyList<LiveStage> Stages, IReadOnlyList<LiveFinding> Findings, string Summary);

/// <summary>Where a run's snapshots and timings go: files in production, a list in tests.</summary>
internal interface ILiveSink
{
    void Publish(LiveSnapshot snapshot);

    void RecordTimings(DateTimeOffset at, IEnumerable<LiveStage> finished);
}

internal static class LiveJson
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    internal static readonly JsonSerializerOptions Compact = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

/// <summary>The "Passed!/Failed! - Failed: 0, Passed: 12, Skipped: 0, Total: 12" line dotnet test ends a project with.</summary>
internal static class TestSummaryLine
{
    internal static (int Passed, int Failed, int Total)? Parse(string? summary)
    {
        if (summary is null)
        {
            return null;
        }

        int? Number(string label)
        {
            var at = summary.IndexOf(label + ":", StringComparison.Ordinal);
            if (at < 0)
            {
                return null;
            }

            var digits = new string(summary[(at + label.Length + 1)..].TrimStart().TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digits, out var n) ? n : null;
        }

        var (passed, failed, total) = (Number("Passed"), Number("Failed"), Number("Total"));
        return passed is null || failed is null || total is null ? null : (passed.Value, failed.Value, total.Value);
    }
}
