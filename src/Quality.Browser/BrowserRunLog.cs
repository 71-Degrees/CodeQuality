/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.Json;

namespace CodeQuality.Browser;

/// <summary>
/// One line per browser-test step, appended to a file and never rewritten or deleted: how long the site took to start, how long each scenario took, how many problems the page had. Read over time it shows slippage
/// (a start that doubled, a scenario that got slow) that no single run would.
/// </summary>
public sealed class BrowserRunLog
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly Lock Gate = new();

    /// <summary>Set (to anything) by <c>quality quick</c> for the test runs it starts: a partial, in-the-middle-of-editing run is not a data point of the record that is read over time.</summary>
    public const string QuickRunVariable = "QUALITY_QUICK";

    private readonly string _path;
    private readonly bool _silent;

    /// <param name="path">The file to append to (created, with its folder, when missing).</param>
    /// <param name="silent">Record nothing. By default: whenever a quick run started this process (<see cref="QuickRunVariable"/>).</param>
    public BrowserRunLog(string path, bool? silent = null)
    {
        _path = path;
        _silent = silent ?? !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(QuickRunVariable));
    }

    public void Append(string name, TimeSpan took, int problems, string? note = null)
    {
        if (_silent)
        {
            return;
        }

        var line = JsonSerializer.Serialize(new { at = DateTimeOffset.Now, name, milliseconds = (long)took.TotalMilliseconds, problems, note }, Json);
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.AppendAllText(_path, line + System.Environment.NewLine);
        }
    }

    /// <summary>Runs a scenario, records how long it took and how many problems its page had, and lets the page's problems fail it.</summary>
    public async Task TimeAsync(string name, PageProbe probe, Func<Task> scenario)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await scenario().ConfigureAwait(false);
            probe.AssertClean();
        }
        finally
        {
            Append(name, started.Elapsed, probe.Problems.Count);
        }
    }
}
