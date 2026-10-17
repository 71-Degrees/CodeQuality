/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.Json;

namespace CodeQuality.Quality;

/// <summary>
/// Where a run's live status lives: <c>reports/quality-live/status.json</c> (the snapshot), <c>status.js</c> (the same snapshot as a script, because a page opened from disk cannot fetch a file but can load
/// a script) and <c>index.html</c> (the page that polls it). The timings of finished steps are appended to <c>reports/quality-run-timings.jsonl</c> for the next run's estimates.
/// </summary>
internal sealed class LiveFiles : ILiveSink
{
    private readonly string _folder;
    private readonly string _timings;
    private bool _complained;

    internal LiveFiles(string reports)
    {
        _folder = Path.Combine(reports, "quality-live");
        _timings = Path.Combine(reports, StageEstimates.OwnFile);
    }

    internal string PagePath => Path.Combine(_folder, "index.html");

    internal string StatusPath => Path.Combine(_folder, "status.json");

    /// <summary>Writes the page (always the current version, so a tool update shows at the next run).</summary>
    internal void WritePage()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(PagePath, LivePage.Html);
    }

    public void Publish(LiveSnapshot snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot, LiveJson.Options);
        Replace(StatusPath, json);
        Replace(Path.Combine(_folder, "status.js"), "window.QUALITY_LIVE = " + json + ";");
    }

    public void RecordTimings(DateTimeOffset at, IEnumerable<LiveStage> finished)
    {
        var lines = finished.Select(s => JsonSerializer.Serialize(new { at, stage = s.Id, seconds = Math.Round((s.FinishedAt!.Value - s.StartedAt!.Value).TotalSeconds, 1), passed = s.State == StageState.Passed }, LiveJson.Compact)).ToList();
        if (lines.Count == 0)
        {
            return;
        }

        Guard(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_timings)!);
            File.AppendAllLines(_timings, lines);
        });
    }

    // Written to a temporary file and moved over the real one, so the page never reads half a snapshot.
    private void Replace(string path, string content) => Guard(() =>
    {
        Directory.CreateDirectory(_folder);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content);
        File.Move(temporary, path, overwrite: true);
    });

    // The live page is a convenience: a file Chrome holds for a moment, or a full disk, must not fail the run. Said once, so a run is not buried in it.
    private void Guard(Action write)
    {
        try
        {
            write();
        }
        catch (IOException ex)
        {
            if (!_complained)
            {
                _complained = true;
                Console.Error.WriteLine("  (the live status could not be written: " + ex.Message + ")");
            }
        }
    }
}
