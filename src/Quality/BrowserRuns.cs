/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Globalization;
using System.Text.Json;
using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>One browser-test scenario over many runs: how long it took last time and the time before, its best, and how many problems the page had last time.</summary>
internal sealed record ScenarioTiming(string Name, int Runs, long LatestMilliseconds, long? PreviousMilliseconds, long BestMilliseconds, int Problems, string At);

/// <summary>
/// Reads the browser tests' run log (<c>Quality.Browser</c>'s <c>BrowserRunLog</c>: one JSON line per scenario run, only ever appended to), so the report can show each scenario's last time against the one before and its
/// best - the slippage a single run never shows. The log's place is the site's <c>browserRunLog</c> setting.
/// </summary>
internal static class BrowserRuns
{
    public static List<ScenarioTiming>? Read(string root, ToolConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.BrowserRunLog))
        {
            return null;
        }

        var path = Path.Combine(Repo.OutputRoot(root), config.BrowserRunLog);
        return File.Exists(path) ? Parse(File.ReadLines(path)) : null;
    }

    internal static List<ScenarioTiming> Parse(IEnumerable<string> lines)
    {
        var byName = new Dictionary<string, List<(DateTimeOffset At, long Milliseconds, int Problems)>>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (TryParse(line, out var name, out var run))
            {
                if (!byName.TryGetValue(name, out var runs))
                {
                    byName[name] = runs = [];
                }

                runs.Add(run);
            }
        }

        return byName
            .Select(kv =>
            {
                var runs = kv.Value;
                var latest = runs[^1];
                return new ScenarioTiming(
                    kv.Key,
                    runs.Count,
                    latest.Milliseconds,
                    runs.Count > 1 ? runs[^2].Milliseconds : null,
                    runs.Min(r => r.Milliseconds),
                    latest.Problems,
                    latest.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
            })
            .ToList();
    }

    private static bool TryParse(string line, out string name, out (DateTimeOffset At, long Milliseconds, int Problems) run)
    {
        name = string.Empty;
        run = default;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var o = doc.RootElement;
            name = o.GetProperty("name").GetString() ?? string.Empty;
            run = (o.GetProperty("at").GetDateTimeOffset(), o.GetProperty("milliseconds").GetInt64(), o.GetProperty("problems").GetInt32());
            return name.Length > 0;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return false;   // a half-written or foreign line in an append-only log is skipped, not fatal
        }
    }
}
