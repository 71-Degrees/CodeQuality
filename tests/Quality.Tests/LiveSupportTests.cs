/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.Json;

namespace CodeQuality.Quality.Tests;

public sealed class LiveSupportTests
{
    [Fact]
    public void TheEstimateIsTheMedianOfTheLastFivePassingRuns()
    {
        var lines = new[] { 500, 10, 12, 11, 13, 14 }.Select(s => $$"""{"stage":"x","seconds":{{s}},"passed":true}""");

        var estimate = new StageEstimates([], lines).For("x");

        Assert.Equal(12, estimate);   // the 500 fell out of the window of five: 10 12 11 13 14
    }

    [Fact]
    public void AFailedRunIsNotAnEstimate_AndJunkLinesAreIgnored()
    {
        var lines = new[] { """{"stage":"x","seconds":999,"passed":false}""", "not json", """{"stage":"x","seconds":20,"passed":true}""", """{"seconds":3}""" };

        Assert.Equal(20, new StageEstimates([], lines).For("x"));
    }

    [Fact]
    public void ThisToolsOwnHistoryWinsOverThePushCheck_WhichIsTheFallback()
    {
        var own = new[] { """{"stage":"code smells","seconds":5,"passed":true}""" };
        var push = new[] { """{"stage":"code smells","seconds":40,"passed":true}""", """{"stage":"tests: A","seconds":60,"passed":true}""" };
        var estimates = new StageEstimates(own, push);

        Assert.Equal(5, estimates.For("code smells"));
        Assert.Equal(60, estimates.For("tests: A"));
        Assert.Null(estimates.For("unknown"));
    }

    [Theory]
    [InlineData("Passed!  - Failed:     0, Passed:  1234, Skipped:     2, Total:  1236, Duration: 12 s", 1234, 0, 1236)]
    [InlineData("Failed!  - Failed:     3, Passed:    10, Skipped:     0, Total:    13, Duration: 1 s", 10, 3, 13)]
    public void TheTestSummaryLineGivesTheCounts(string line, int passed, int failed, int total)
    {
        Assert.Equal((passed, failed, total), TestSummaryLine.Parse(line));
    }

    [Fact]
    public void ALineWithoutCountsGivesNone()
    {
        Assert.Null(TestSummaryLine.Parse(null));
        Assert.Null(TestSummaryLine.Parse("did not complete"));
    }

    [Fact]
    public void FilesAreWrittenWholeAndTheScriptCarriesTheSameSnapshot()
    {
        var root = Directory.CreateTempSubdirectory("cq-live-").FullName;
        try
        {
            var files = new LiveFiles(Path.Combine(root, "reports"));
            files.WritePage();
            using var run = new LiveRun("tests", files, StageEstimates.None, () => DateTimeOffset.Now);
            run.Declare("a", "A");

            var json = File.ReadAllText(files.StatusPath);
            var script = File.ReadAllText(Path.Combine(Path.GetDirectoryName(files.StatusPath)!, "status.js"));

            Assert.Equal("a", JsonDocument.Parse(json).RootElement.GetProperty("stages")[0].GetProperty("id").GetString());
            Assert.Equal("window.QUALITY_LIVE = " + json + ";", script);
            Assert.False(File.Exists(files.StatusPath + ".tmp"));
            Assert.True(File.Exists(files.PagePath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task FinishedStepsAreAppendedToTheOwnTimingLog_WhichTheNextRunReadsAsEstimates()
    {
        var root = Directory.CreateTempSubdirectory("cq-live-").FullName;
        try
        {
            var now = DateTimeOffset.Now;
            using (var run = new LiveRun("smells", new LiveFiles(Path.Combine(root, "reports")), StageEstimates.None, () => now))
            {
                await run.RunStageAsync("code smells", "Code smells", () => { now = now.AddSeconds(8); return Task.FromResult(0); });
                run.Complete(0);
            }

            Assert.Equal(8, StageEstimates.Load(Path.Combine(root, "reports")).For("code smells"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ThePagePollsTheStatusScriptEveryTwoSecondsAndStopsWhenTheRunEnds()
    {
        Assert.Contains("status.js", LivePage.Html);
        Assert.Contains("setInterval(poll, 2000)", LivePage.Html);
        Assert.Contains("clearInterval(timer)", LivePage.Html);
    }
}
