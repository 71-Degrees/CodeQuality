/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.Json;

namespace CodeQuality.Quality.Tests;

public sealed class LiveRunTests
{
    private sealed class ListSink : ILiveSink
    {
        public List<LiveSnapshot> Snapshots { get; } = [];

        public List<LiveStage> Timed { get; } = [];

        public void Publish(LiveSnapshot snapshot)
        {
            lock (Snapshots)
            {
                Snapshots.Add(snapshot);
            }
        }

        public void RecordTimings(DateTimeOffset at, IEnumerable<LiveStage> finished) => Timed.AddRange(finished);

        public LiveSnapshot Last
        {
            get
            {
                lock (Snapshots)
                {
                    return Snapshots[^1];
                }
            }
        }
    }

    private static readonly DateTimeOffset Start = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);

    private static (LiveRun Run, ListSink Sink, Func<DateTimeOffset> Clock, Action<double> Advance) Make(StageEstimates? estimates = null)
    {
        var now = Start;
        var sink = new ListSink();
        var run = new LiveRun("quick", sink, estimates ?? StageEstimates.None, () => now);
        return (run, sink, () => now, seconds => now = now.AddSeconds(seconds));
    }

    private static Finding Found(string rule = "SMELL1", int value = 70) => new(rule, "src/A.cs", "M", 12, value, "too long");

    [Fact]
    public void ARunIsPublishedAtOnce_AsRunningWithNoSteps()
    {
        var (run, sink, _, _) = Make();
        using var disposable = run;

        Assert.Equal(RunState.Running, sink.Last.State);
        Assert.Empty(sink.Last.Stages);
    }

    [Fact]
    public async Task AStepShowsRunningWhileItWorksAndPassedWhenItReturnsZero()
    {
        var (run, sink, _, advance) = Make();
        using var disposable = run;
        StageState during = default;

        var code = await run.RunStageAsync("a", "Step A", () =>
        {
            during = sink.Last.Stages.Single().State;
            advance(12);
            return Task.FromResult(0);
        });

        var stage = sink.Last.Stages.Single();
        Assert.Equal(0, code);
        Assert.Equal(StageState.Running, during);
        Assert.Equal(StageState.Passed, stage.State);
        Assert.Equal(12, (stage.FinishedAt - stage.StartedAt)!.Value.TotalSeconds);
    }

    [Theory]
    [InlineData(1, "Failed")]
    [InlineData(2, "Error")]
    public async Task ANonZeroExitCodeIsAFailureOrAnError(int exit, string expected)
    {
        var (run, sink, _, _) = Make();
        using var disposable = run;

        await run.RunStageAsync("a", "Step A", () => Task.FromResult(exit));

        Assert.Equal(expected, sink.Last.Stages.Single().State.ToString());
    }

    [Fact]
    public async Task AStepThatThrowsIsMarkedAnErrorAndTheExceptionGoesOn()
    {
        var (run, sink, _, _) = Make();
        using var disposable = run;

        await Assert.ThrowsAsync<InvalidOperationException>(() => run.RunStageAsync("a", "Step A", () => throw new InvalidOperationException("boom")));

        Assert.Equal(StageState.Error, sink.Last.Stages.Single().State);
    }

    [Fact]
    public void DeclaredStepsAreListedPendingInOrder_AndDeclaringTwiceChangesNothing()
    {
        var (run, sink, _, _) = Make();
        using var disposable = run;

        run.Declare("a", "A");
        run.Declare("b", "B");
        run.Declare("a", "A again");

        Assert.Equal(["A", "B"], sink.Last.Stages.Select(s => s.Label));
        Assert.All(sink.Last.Stages, s => Assert.Equal(StageState.Pending, s.State));
    }

    [Fact]
    public void SkippedStepsSayWhy()
    {
        var (run, sink, _, _) = Make();
        using var disposable = run;

        run.Skip([("t2", "Tests 2")], "an earlier project failed");

        Assert.Equal(StageState.Skipped, sink.Last.Stages.Single().State);
        Assert.Equal("an earlier project failed", sink.Last.Stages.Single().Detail);
    }

    [Fact]
    public void FindingsAreKeptWithTheirKindAndStage()
    {
        var (run, sink, _, _) = Make();
        using var disposable = run;

        run.AddFindings("smells", [Found("A")], [(Found("B", 90), 70)]);

        Assert.Equal(["new", "worse"], sink.Last.Findings.Select(f => f.Kind));
        Assert.Contains("was 70, now 90", sink.Last.Findings[1].Message);
        Assert.All(sink.Last.Findings, f => Assert.Equal("smells", f.Stage));
    }

    [Fact]
    public async Task TheEstimateIsTheRestOfTheRunningStepPlusTheStepsNotStarted()
    {
        var estimates = new StageEstimates([], ["""{"stage":"a","seconds":100,"passed":true}""", """{"stage":"b","seconds":50,"passed":true}"""]);
        var (run, sink, _, advance) = Make(estimates);
        using var disposable = run;
        run.Declare("a", "A");
        run.Declare("b", "B");

        DateTimeOffset? finish = null;
        await run.RunStageAsync("a", "A", () =>
        {
            advance(30);
            run.Note("a", "tick");
            finish = sink.Last.EstimatedFinishAt;
            return Task.FromResult(0);
        });

        Assert.Equal(Start.AddSeconds(30 + 70 + 50), finish);
    }

    [Fact]
    public void NoEstimateIsGivenWhenNothingIsKnown()
    {
        var (run, sink, _, _) = Make();
        using var disposable = run;
        run.Declare("a", "A");

        Assert.Null(sink.Last.EstimatedFinishAt);
    }

    [Fact]
    public async Task CompletingTheRunGivesTheVerdictAndTheConsoleLine_AndIsPublishedLast()
    {
        var (run, sink, _, advance) = Make();
        using var disposable = run;
        await run.RunStageAsync("a", "A", () => { advance(75); return Task.FromResult(1); });
        run.AddFindings("a", [Found()], []);

        var line = run.Complete(1);

        Assert.Equal(RunState.Failed, sink.Last.State);
        Assert.Equal("Something new or worse needs a look.", sink.Last.Verdict);
        Assert.Equal(line, sink.Last.Summary);
        Assert.Equal("Quality quick: Something new or worse needs a look - 1 step(s) in 1m 15s, 1 failed; 1 new and 0 worse finding(s).", line);
        Assert.NotNull(sink.Last.FinishedAt);
        Assert.Null(sink.Last.EstimatedFinishAt);
    }

    [Theory]
    [InlineData(0, "Passed", "All clear.")]
    [InlineData(2, "Error", "A check could not run.")]
    public void TheExitCodeDecidesTheVerdict_TheSameWordsAsTheConsole(int exit, string state, string verdict)
    {
        var (run, sink, _, _) = Make();
        using var disposable = run;

        run.Complete(exit);

        Assert.Equal(state, sink.Last.State.ToString());
        Assert.Equal(verdict, sink.Last.Verdict);
    }

    [Fact]
    public async Task OnlyStepsThatPassedOrFailedAreTimed_NotSkippedOrErroredOnes()
    {
        var (run, sink, _, advance) = Make();
        using var disposable = run;
        await run.RunStageAsync("ok", "ok", () => { advance(5); return Task.FromResult(0); });
        await run.RunStageAsync("bad", "bad", () => { advance(5); return Task.FromResult(1); });
        await run.RunStageAsync("broken", "broken", () => { advance(5); return Task.FromResult(2); });
        run.Skip([("later", "later")], "x");

        run.Complete(1);

        Assert.Equal(["ok", "bad"], sink.Timed.Select(s => s.Id));
    }

    [Fact]
    public void AbortingEndsTheRunAsAnErrorWithTheReason_AndMarksARunningStepFailed()
    {
        var (run, sink, _, _) = Make();
        using var disposable = run;
        var pending = run.RunStageAsync("a", "A", () => new TaskCompletionSource<int>().Task);
        Assert.False(pending.IsCompleted);

        run.Abort("it fell over");

        Assert.Equal(RunState.Error, sink.Last.State);
        Assert.Contains("it fell over", sink.Last.Verdict);
        Assert.Equal(StageState.Error, sink.Last.Stages.Single().State);
    }

    [Fact]
    public async Task ARunWithoutASinkRecordsNothingAndStillRunsTheWork()
    {
        var ran = false;

        var code = await LiveRun.Off.RunStageAsync("a", "A", () => { ran = true; return Task.FromResult(0); });

        Assert.True(ran);
        Assert.Equal(0, code);
    }

    [Fact]
    public async Task TheSnapshotSerializesWithWordsForStates_SoThePageCanReadIt()
    {
        var (run, sink, _, _) = Make();
        using var disposable = run;
        await run.RunStageAsync("a", "A", () => Task.FromResult(0));

        var json = JsonDocument.Parse(JsonSerializer.Serialize(sink.Last, LiveJson.Options)).RootElement;

        Assert.Equal("running", json.GetProperty("state").GetString());
        Assert.Equal("passed", json.GetProperty("stages")[0].GetProperty("state").GetString());
    }
}
