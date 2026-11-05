/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Quality;

namespace CodeQuality.Quality.Tests;

public class HistoryTests
{
    private static readonly DateTime Noon = new(2026, 10, 9, 12, 0, 0);

    private static Finding Smell(string rule, int value, string member = "A.B") => new(rule, "src/A.cs", member, 1, value, "m");

    private static Collected Ran(string check, params Finding[] findings)
    {
        var collected = new Collected();
        if (check == "smells")
        {
            collected.Smells = findings;
        }
        else
        {
            collected.Analyzers = findings;
        }

        collected.RanThisRun.Add(check);
        return collected;
    }

    [Fact]
    public void PressureWeighsHowFarOverTheLimitAFindingIs()
    {
        Assert.Equal(1.017, Weights.Of("LongMethod", 61), 3);
        Assert.Equal(1, Weights.Of("LongMethod", 20));
        Assert.Equal(2.5, Weights.Of("LongMethod", 150));
        Assert.Equal(1, Weights.Of("EmptyCatch", 1));
        Assert.Equal(1.5, Weights.Of("build/CA1305", 5), 3);
    }

    [Fact]
    public void CountsTallyTheTotalPressureAndEachRule()
    {
        var counts = CheckCounts.From(new[] { Smell("LongMethod", 120, "A.One"), Smell("LongMethod", 90, "A.Two"), Smell("Complexity", 30) });

        Assert.Equal(3, counts.Total);
        Assert.Equal(5.5, counts.Pressure);
        Assert.Equal(2, counts.ByRule["LongMethod"]);
        Assert.Equal(1, counts.ByRule["Complexity"]);
    }

    [Fact]
    public void ARunThatChangedTheNumbersAddsASnapshotAndOneThatDidNotDoesNot()
    {
        var history = new History();

        Assert.True(history.Record(Ran("smells", Smell("LongMethod", 120)), "aaa1111", Noon));
        Assert.False(history.Record(Ran("smells", Smell("LongMethod", 120)), "bbb2222", Noon.AddHours(1)));
        Assert.True(history.Record(Ran("smells"), "ccc3333", Noon.AddHours(2)));

        Assert.Equal(2, history.Snapshots.Count);
        Assert.Equal(0, history.Snapshots[^1].Smells!.Total);
    }

    [Fact]
    public void ACheckThatDidNotRunKeepsItsEarlierNumbers()
    {
        var history = new History();
        history.Record(Ran("smells", Smell("LongMethod", 120)), "aaa1111", Noon);

        history.Record(Ran("analyzers", new Finding("build/CA1305", "src/A.cs", "(file)", 1, 4, "m")), "bbb2222", Noon.AddHours(1));

        var latest = history.Snapshots[^1];
        Assert.Equal(1, latest.Smells!.Total);
        Assert.Equal(1, latest.Analyzers!.Total);
    }

    [Fact]
    public void NothingIsRecordedWhenNoCheckRan()
    {
        Assert.False(new History().Record(new Collected(), "aaa1111", Noon));
    }

    [Fact]
    public void TheSummaryNamesWhereItStartedAndWhereItIsNow()
    {
        var history = new History();
        history.Record(Ran("smells", Smell("LongMethod", 120), Smell("Complexity", 30)), "aaa1111", Noon);
        history.Record(Ran("smells", Smell("LongMethod", 120)), "bbb2222", Noon.AddHours(1));

        var summary = history.Summary();

        Assert.NotNull(summary);
        Assert.Contains("code smells 2 -> 1", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ASingleSnapshotHasNothingToCompareYet()
    {
        var history = new History();
        history.Record(Ran("smells", Smell("LongMethod", 120)), "aaa1111", Noon);

        Assert.Null(history.Summary());
    }

    [Fact]
    public void ARunStartsFromTheCommittedTimeline_AndSavesOnlyToTheReportsFolder_SoTheCheckoutStaysClean()
    {
        var root = Directory.CreateTempSubdirectory("quality-history-").FullName;
        try
        {
            var start = new History();
            start.Record(Ran("smells", Smell("LongMethod", 120)), "aaa1111", Noon);
            JsonState.Save(History.StartPath(root), start);
            var committed = File.ReadAllText(History.StartPath(root));

            var loaded = History.Load(root);
            loaded.Record(Ran("smells", Smell("LongMethod", 90)), "bbb2222", Noon.AddDays(1));
            loaded.Save(root);

            Assert.Equal(committed, File.ReadAllText(History.StartPath(root)));
            Assert.Equal(2, History.Load(root).Snapshots.Count);
            Assert.Equal(Path.Combine(root, "reports", "quality-history.json"), History.PathFor(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
