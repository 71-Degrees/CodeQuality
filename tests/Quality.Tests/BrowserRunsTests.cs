/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;
using CodeQuality.Quality;

namespace CodeQuality.Quality.Tests;

public sealed class BrowserRunsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cq-runs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static string Line(string name, long milliseconds, int problems = 0, string at = "2026-10-12T09:00:00-04:00") =>
        $"{{\"at\":\"{at}\",\"name\":\"{name}\",\"milliseconds\":{milliseconds},\"problems\":{problems},\"note\":null}}";

    [Fact]
    public void EachScenarioShowsItsLastTimeTheOneBeforeItsBestAndTheRunCount()
    {
        var timings = BrowserRuns.Parse([Line("join", 1500), Line("editor", 2600), Line("join", 900), Line("join", 1200, problems: 2)]);

        var join = timings.Single(t => t.Name == "join");
        Assert.Equal(3, join.Runs);
        Assert.Equal(1200, join.LatestMilliseconds);
        Assert.Equal(900, join.PreviousMilliseconds);
        Assert.Equal(900, join.BestMilliseconds);
        Assert.Equal(2, join.Problems);

        var editor = timings.Single(t => t.Name == "editor");
        Assert.Equal(1, editor.Runs);
        Assert.Null(editor.PreviousMilliseconds);
    }

    [Fact]
    public void AHalfWrittenOrForeignLineIsSkipped()
    {
        var timings = BrowserRuns.Parse([Line("join", 1000), "{\"at\":\"2026-10-12", "not json at all", "{\"name\":\"no times\"}", Line("join", 1100)]);

        Assert.Equal(2, Assert.Single(timings).Runs);
    }

    [Fact]
    public void WithNoSettingOrNoFileThereIsNothingToShow()
    {
        Assert.Null(BrowserRuns.Read(_root, new ToolConfig()));
        Assert.Null(BrowserRuns.Read(_root, new ToolConfig { BrowserRunLog = "reports/runs.jsonl" }));
    }

    [Fact]
    public void TheLogIsReadFromTheConfiguredPlace()
    {
        var path = Path.Combine(_root, "reports", "runs.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, [Line("join", 1000)]);

        var timings = BrowserRuns.Read(_root, new ToolConfig { BrowserRunLog = "reports/runs.jsonl" })!;

        Assert.Equal("join", Assert.Single(timings).Name);
    }

    [Theory]
    [InlineData("TriviaNight.Browser.Tests", true)]
    [InlineData("Site.browser.Tests", true)]
    [InlineData("TriviaNight.Core.Tests", false)]
    public void ATestProjectWithBrowserInItsNameIsABrowserProject(string name, bool expected) =>
        Assert.Equal(expected, TestResults.IsBrowserProject(name));

    [Fact]
    public void BrowserResultsStayOutOfTheUnitTotalAndTheCoverage()
    {
        const string Trx = """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Times start="2026-10-12T10:00:00Z" finish="2026-10-12T10:00:01Z" />
              <ResultSummary><Counters total="9" executed="9" passed="9" failed="0" /></ResultSummary></TestRun>
            """;
        foreach (var (name, total) in new[] { ("Core.Tests", 4), ("Site.Browser.Tests", 9) })
        {
            var folder = Path.Combine(TestResults.ResultsFolder(_root), name);
            Directory.CreateDirectory(Path.Combine(folder, "run"));
            File.WriteAllText(Path.Combine(folder, "results.trx"), Trx.Replace("total=\"9\"", $"total=\"{total}\"", StringComparison.Ordinal).Replace("passed=\"9\"", $"passed=\"{total}\"", StringComparison.Ordinal));
            File.WriteAllText(
                Path.Combine(folder, "run", "coverage.cobertura.xml"),
                $"<coverage><packages><package><classes><class filename=\"{_root}/src/Core/{name}.cs\"><lines><line number=\"1\" hits=\"1\" /></lines></class></classes></package></packages></coverage>");
        }

        var summary = TestResults.Read(_root, new ToolConfig())!;

        Assert.Equal(4, summary.Total);
        Assert.Equal(9, Assert.Single(summary.Browser).Total);
        Assert.Equal(1, summary.CoverableLines);   // only the unit project's file
    }
}
