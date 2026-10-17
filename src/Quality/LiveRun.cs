/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Quality;

/// <summary>
/// The record of one run as it happens: which steps there are, which is running, what each found, how long each took against the usual. Every change is published whole to a sink (the live page reads it),
/// and a timer republishes every few seconds so the page can tell a run that is quiet from one that died. With no sink (<see cref="Off"/>) it records nothing and costs nothing.
/// </summary>
internal sealed class LiveRun : IDisposable
{
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly ILiveSink? _sink;
    private readonly StageEstimates _estimates;
    private readonly Func<DateTimeOffset> _clock;
    private readonly List<LiveStage> _stages = [];
    private readonly List<LiveFinding> _findings = [];
    private readonly string _command;
    private readonly string _runId;
    private readonly DateTimeOffset _startedAt;
    private readonly Timer? _beat;
    private RunState _state = RunState.Running;
    private string _verdict = "Running";
    private DateTimeOffset? _finishedAt;

    internal LiveRun(string command, ILiveSink? sink, StageEstimates estimates, Func<DateTimeOffset> clock)
    {
        (_command, _sink, _estimates, _clock) = (command, sink, estimates, clock);
        _startedAt = clock();
        _runId = _startedAt.ToString("yyyyMMddTHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        if (sink is not null)
        {
            Publish();
            _beat = new Timer(_ => Beat(), null, Heartbeat, Heartbeat);
        }
    }

    /// <summary>A run that records nothing: what every command without a live page uses.</summary>
    internal static LiveRun Off { get; } = new("off", null, StageEstimates.None, () => DateTimeOffset.Now);

    /// <summary>Lists a step before it starts, so the page shows what is still to come. Declaring one twice changes nothing.</summary>
    internal void Declare(string id, string label)
    {
        lock (_gate)
        {
            if (IndexOf(id) < 0)
            {
                _stages.Add(new LiveStage(id, label, StageState.Pending, null, null, _estimates.For(id), string.Empty, null, null, null));
                Publish();
            }
        }
    }

    /// <summary>Runs a step, timing it. Its exit code is the tool's own: 0 passed, 1 something new or failing, 2 it could not run; an exception marks it as an error and goes on up.</summary>
    internal async Task<int> RunStageAsync(string id, string label, Func<Task<int>> work)
    {
        Declare(id, label);
        Change(id, s => s with { State = StageState.Running, StartedAt = _clock() });
        var code = 2;   // stays 2 if the work throws
        try
        {
            code = await work();
            return code;
        }
        finally
        {
            Change(id, s => s with { State = code == 0 ? StageState.Passed : code == 1 ? StageState.Failed : StageState.Error, FinishedAt = _clock() });
        }
    }

    internal void Note(string id, string detail) => Change(id, s => s with { Detail = detail });

    internal void Counts(string id, (int Passed, int Failed, int Total)? counts) =>
        Change(id, s => counts is { } c ? s with { Passed = c.Passed, Failed = c.Failed, Total = c.Total } : s);

    /// <summary>Marks steps that will not run (a run that stopped at its first failure).</summary>
    internal void Skip(IEnumerable<(string Id, string Label)> steps, string reason)
    {
        foreach (var (id, label) in steps)
        {
            Declare(id, label);
            Change(id, s => s with { State = StageState.Skipped, Detail = reason });
        }
    }

    internal void AddFindings(string stage, IEnumerable<Finding> added, IEnumerable<(Finding Finding, int Was)> worse)
    {
        lock (_gate)
        {
            _findings.AddRange(added.Select(f => new LiveFinding(stage, "new", f.Rule, f.File, f.Line, f.Message)));
            _findings.AddRange(worse.Select(w => new LiveFinding(stage, "worse", w.Finding.Rule, w.Finding.File, w.Finding.Line, $"was {w.Was}, now {w.Finding.Value}: {w.Finding.Message}")));
            Publish();
        }
    }

    /// <summary>Ends the run with the tool's exit code and returns the one line the console prints.</summary>
    internal string Complete(int exitCode)
    {
        lock (_gate)
        {
            _state = exitCode switch { 0 => RunState.Passed, 1 => RunState.Failed, _ => RunState.Error };
            _verdict = exitCode switch { 0 => "All clear.", 1 => "Something new or worse needs a look.", _ => "A check could not run." };
            return End();
        }
    }

    /// <summary>Ends the run because something threw: the page shows the reason instead of a run that seems to go on.</summary>
    internal void Abort(string reason)
    {
        lock (_gate)
        {
            _state = RunState.Error;
            _verdict = "The run stopped: " + reason;
            End();
        }
    }

    public void Dispose() => _beat?.Dispose();

    private string End()
    {
        _beat?.Dispose();
        _finishedAt = _clock();
        for (var i = 0; i < _stages.Count; i++)
        {
            if (_stages[i].State == StageState.Running)
            {
                _stages[i] = _stages[i] with { State = StageState.Error, FinishedAt = _finishedAt };
            }
        }

        Publish();
        _sink?.RecordTimings(_startedAt, _stages.Where(s => (s.State is StageState.Passed or StageState.Failed) && s.StartedAt is not null && s.FinishedAt is not null));
        return Summary();
    }

    // The console line: the verdict, how long, how the steps went and how many findings are new or worse.
    private string Summary()
    {
        var took = (_finishedAt ?? _clock()) - _startedAt;
        var ran = _stages.Count(s => s.State is not (StageState.Pending or StageState.Skipped));
        var failed = _stages.Count(s => s.State is StageState.Failed or StageState.Error);
        var skipped = _stages.Count(s => s.State == StageState.Skipped);
        var worse = _findings.Count(f => f.Kind == "worse");
        return $"Quality {_command}: {_verdict.TrimEnd('.')} - {ran} step(s) in {Duration(took)}, {failed} failed{(skipped > 0 ? $", {skipped} not run" : string.Empty)}; {_findings.Count - worse} new and {worse} worse finding(s).";
    }

    internal static string Duration(TimeSpan span) => span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes}m {span.Seconds:00}s" : $"{span.TotalSeconds:0.#}s";

    private void Change(string id, Func<LiveStage, LiveStage> edit)
    {
        lock (_gate)
        {
            var at = IndexOf(id);
            if (at >= 0)
            {
                _stages[at] = edit(_stages[at]);
                Publish();
            }
        }
    }

    private int IndexOf(string id) => _stages.FindIndex(s => s.Id == id);

    private void Beat()
    {
        lock (_gate)
        {
            if (_state == RunState.Running)
            {
                Publish();
            }
        }
    }

    // What is left to do, from the usual times: the rest of the running step plus every step not yet started. Null when nothing is known about any of them.
    private DateTimeOffset? EstimatedFinish(DateTimeOffset now)
    {
        if (_state != RunState.Running)
        {
            return null;
        }

        var left = 0.0;
        var known = false;
        foreach (var stage in _stages.Where(s => (s.State is StageState.Pending or StageState.Running) && s.EstimateSeconds is not null))
        {
            known = true;
            left += stage.State == StageState.Running ? Math.Max(stage.EstimateSeconds!.Value - (now - stage.StartedAt!.Value).TotalSeconds, 0) : stage.EstimateSeconds!.Value;
        }

        return known ? now.AddSeconds(left) : null;
    }

    private void Publish()
    {
        if (_sink is null)
        {
            return;
        }

        var now = _clock();
        _sink.Publish(new LiveSnapshot(_runId, _command, _state, _verdict, _startedAt, _finishedAt, now, EstimatedFinish(now), [.. _stages], [.. _findings], Summary()));
    }
}
