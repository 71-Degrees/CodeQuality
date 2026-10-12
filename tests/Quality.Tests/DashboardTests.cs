/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Quality;

namespace CodeQuality.Quality.Tests;

public class DashboardTests
{
    private static Snapshot Snap(string commit, string at, int longMethods, int duplicates = 0, int? tests = null, double? coverage = null) =>
        new(
            at,
            commit,
            string.Empty,
            new CheckCounts(longMethods + duplicates, longMethods + duplicates, new Dictionary<string, int> { ["LongMethod"] = longMethods, ["DuplicateCode"] = duplicates }, new Dictionary<string, double> { ["LongMethod"] = longMethods, ["DuplicateCode"] = duplicates }),
            null,
            tests is null ? null : new TestCounts(tests.Value, 0, coverage));

    private static History HistoryOf(params Snapshot[] snapshots) => new() { Snapshots = [.. snapshots] };

    [Fact]
    public void ACommitMeasuredSeveralTimesKeepsItsLastMeasurement()
    {
        var points = Dashboard.Points(HistoryOf(Snap("aaa", "2026-10-01 09:00", 10), Snap("aaa", "2026-10-01 09:30", 8), Snap("bbb", "2026-10-02 09:00", 7)));

        Assert.Equal(["aaa", "bbb"], points.Select(p => p.Commit));
        Assert.Equal([8, 7], points.Select(p => p.Smells));
    }

    [Fact]
    public void TheHeadlineLeavesOutRulesAddedLater_ButTheAllRulesLineCountsThem()
    {
        var points = Dashboard.Points(HistoryOf(Snap("aaa", "2026-10-01 09:00", longMethods: 10, duplicates: 90)));

        Assert.Equal(10, points[0].Smells);
        Assert.Equal(100, points[0].SmellsAll);
    }

    [Fact]
    public void TestsAndCoverageRideAlongWithTheSnapshot_AndAreNullWhenNotMeasured()
    {
        var points = Dashboard.Points(HistoryOf(Snap("aaa", "2026-10-01 09:00", 5), Snap("bbb", "2026-10-02 09:00", 5, tests: 120, coverage: 0.7)));

        Assert.Null(points[0].Tests);
        Assert.Equal(120, points[1].Tests);
        Assert.Equal(0.7, points[1].Coverage);
    }

    [Fact]
    public void ACommitKeepsItsFullestTestNumbers_WhateverOrderTheRunsCameIn()
    {
        var small = Snap("aaa", "2026-10-01 09:00", 5, tests: 100, coverage: 0.7);
        var full = Snap("aaa", "2026-10-01 09:05", 5, tests: 150, coverage: 0.5);

        Assert.Equal(150, Dashboard.Points(HistoryOf(small, full))[0].Tests);
        Assert.Equal(150, Dashboard.Points(HistoryOf(full, small))[0].Tests);
        Assert.Equal(0.5, Dashboard.Points(HistoryOf(full, small))[0].Coverage);
    }

    [Fact]
    public void ACommitThatOnlyHadTheSmallRunShowsNoTestNumbersInsteadOfADip()
    {
        var points = Dashboard.Points(HistoryOf(
            Snap("aaa", "2026-10-01 09:00", 5, tests: 150, coverage: 0.5),
            Snap("bbb", "2026-10-02 09:00", 5, tests: 100, coverage: 0.7),
            Snap("ccc", "2026-10-03 09:00", 5, tests: 152, coverage: 0.5)));

        Assert.Equal([150, null, 152], points.Select(p => p.Tests));
        Assert.Null(points[1].Coverage);
    }

    [Fact]
    public void AGrowingTestSuiteIsNeverHidden()
    {
        var points = Dashboard.Points(HistoryOf(Snap("a", "2026-10-01 09:00", 5, tests: 10), Snap("b", "2026-10-02 09:00", 5, tests: 40), Snap("c", "2026-10-03 09:00", 5, tests: 160)));

        Assert.Equal([10, 40, 160], points.Select(p => p.Tests));
    }

    [Fact]
    public void ACommitTakesTheLowestCoverageEitherRunRecorded()
    {
        var small = Snap("aaa", "2026-10-01 09:00", 5, tests: 100, coverage: 0.72);
        var full = Snap("aaa", "2026-10-01 09:05", 5, tests: 150, coverage: 0.48);

        Assert.Equal(0.48, Dashboard.Points(HistoryOf(small, full))[0].Coverage);
        Assert.Equal(0.48, Dashboard.Points(HistoryOf(full, small))[0].Coverage);
    }

    [Fact]
    public void AHeadlineCoverageNeverRisesAboveTheLowestOfTheLastFewCommits()
    {
        var history = HistoryOf(
            Snap("a", "2026-10-01 09:00", 5, tests: 150, coverage: 0.48),
            Snap("b", "2026-10-02 09:00", 5, tests: 100, coverage: 0.72));

        Assert.Equal(0.48, Dashboard.ConservativeCoverage(history, 0.72));
        Assert.Equal(0.40, Dashboard.ConservativeCoverage(history, 0.40));
        Assert.Equal(0.72, Dashboard.ConservativeCoverage(HistoryOf(Snap("b", "2026-10-02 09:00", 5, tests: 100, coverage: 0.72)), null));
        Assert.Null(Dashboard.ConservativeCoverage(new History(), null));
    }

    [Fact]
    public void APushIsItsStagesTogether_WithTheSlowestOneAsItsTime()
    {
        string[] lines =
        [
            """{"at":"2026-10-12T11:45:36-04:00","stage":"tests: Web","seconds":28.1,"passed":true}""",
            """{"at":"2026-10-12T11:45:36-04:00","stage":"code smells","seconds":19.3,"passed":false}""",
            """{"at":"2026-10-12T12:10:00-04:00","stage":"tests: Web","seconds":30,"passed":true}""",
        ];

        var runs = Dashboard.GateRuns(lines);

        Assert.Equal(2, runs.Count);
        Assert.Equal(28.1, runs[0].SlowestSeconds);
        Assert.False(runs[0].Passed);
        Assert.True(runs[1].Passed);
    }

    [Fact]
    public void ALineThatIsNotAStageIsSkipped()
    {
        var runs = Dashboard.GateRuns(["not json", """{"at":"x"}""", """{"at":"2026-10-12T11:45:36-04:00","stage":"s","seconds":1,"passed":true}"""]);

        Assert.Single(runs);
    }

    [Fact]
    public void ThePageCarriesTheTitleTheCommitAndTheData()
    {
        var html = Dashboard.Render("Better Trivia", "abc1234", HistoryOf(Snap("aaa", "2026-10-01 09:00", 5), Snap("bbb", "2026-10-02 09:00", 4)), [new GateRun("2026-10-12T11:45:36-04:00", 30, true)]);

        Assert.Contains("Better Trivia - code quality dashboard", html, StringComparison.Ordinal);
        Assert.Contains("\"commit\":\"abc1234\"", html, StringComparison.Ordinal);
        Assert.Contains("\"smells\":4", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/*DATA*/", html, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyTimelineStillRendersAPage()
    {
        var html = Dashboard.Render("Site", "abc", new History(), []);

        Assert.Contains("\"points\":[]", html, StringComparison.Ordinal);
    }
}
