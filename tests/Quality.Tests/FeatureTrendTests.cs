/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Quality;

namespace CodeQuality.Quality.Tests;

public class FeatureTrendTests
{
    // Written the way the real file is: comments and a trailing comma are allowed.
    private const string Registry = """
        {
          // the curated list
          "functions": [
            { "id": "a", "name": "A", "walked": true,  "tests": [ "JoinBrowserTests", "JoinTests" ] },
            { "id": "b", "name": "B", "walked": false, "tests": [ "PartlyBrowserTests" ] },
            { "id": "c", "name": "C", "walked": false, "tests": [ "UnitTests" ], },
          ]
        }
        """;

    [Fact]
    public void CountsTheFunctionsTheWalkedOnesAndThoseWithAnyBrowserTest()
    {
        Assert.Equal((3, 1, 2), FeatureTrend.Count(Registry));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoFileTextMeansNoNumbers(string? text)
    {
        Assert.Null(FeatureTrend.Count(text));
    }

    [Fact]
    public async Task EachCommitGetsItsOwnNumbers_InOrder_AndACommitWithoutTheFileIsLeftOut()
    {
        var points = await FeatureTrend.PointsAsync(
            [("old", "2026-10-01 09:00"), ("mid", "2026-10-05 09:00"), ("new", "2026-10-09 09:00")],
            commit => Task.FromResult<string?>(commit == "mid" ? Registry.Replace("\"walked\": false, \"tests\": [ \"UnitTests\" ]", "\"walked\": true, \"tests\": [ \"UnitTests\" ]", StringComparison.Ordinal) : commit == "new" ? Registry : null));

        Assert.Equal(["mid", "new"], points.Select(p => p.Commit));
        Assert.Equal([2, 1], points.Select(p => p.Walked));
        Assert.Equal("2026-10-05 09:00", points[0].At);
    }

    [Fact]
    public void TheDashboardCarriesTheListsProgressAndDrawsNothingOnItsOwnWhenThereIsNone()
    {
        var with = Dashboard.Render("Site", "abc", new History(), [], new Dashboard.Extras(Features: [new FeaturePoint("abc1234", "2026-10-09 09:00", 63, 41, 44)]));
        var without = Dashboard.Render("Site", "abc", new History(), []);

        Assert.Contains("\"features\":[{\"commit\":\"abc1234\",\"at\":\"2026-10-09 09:00\",\"functions\":63,\"walked\":41,\"withBrowserTest\":44}]", with, StringComparison.Ordinal);
        Assert.Contains("\"features\":null", without, StringComparison.Ordinal);
    }
}
