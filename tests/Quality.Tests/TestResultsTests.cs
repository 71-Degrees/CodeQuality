/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;
using CodeQuality.Quality;

namespace CodeQuality.Quality.Tests;

public sealed class TestResultsTests : IDisposable
{
    private const string Trx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Times creation="2026-10-12T10:00:00.0000000-04:00" start="2026-10-12T10:00:00.0000000-04:00" finish="2026-10-12T10:00:12.5000000-04:00" />
          <Results>
            <UnitTestResult testName="Rules.AreKept" outcome="Passed" />
            <UnitTestResult testName="Rules.AreBroken" outcome="Failed" />
            <UnitTestResult testName="Rules.AreSkipped" outcome="NotExecuted" />
          </Results>
          <ResultSummary outcome="Failed">
            <Counters total="3" executed="2" passed="1" failed="1" error="0" timeout="0" aborted="0" inconclusive="0" notExecuted="1" />
          </ResultSummary>
        </TestRun>
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "cq-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static string Cobertura(string file, params (int Line, int Hits)[] lines) =>
        $"<coverage><packages><package><classes><class filename=\"{file}\"><lines>" + string.Concat(lines.Select(l => $"<line number=\"{l.Line}\" hits=\"{l.Hits}\" />")) + "</lines></class></classes></package></packages></coverage>";

    [Fact]
    public void ATrxFileGivesTheCountsTheFailuresAndTheTime()
    {
        var project = TestResults.ParseTrx("Core.Tests", Trx);

        Assert.Equal(3, project.Total);
        Assert.Equal(1, project.Passed);
        Assert.Equal(1, project.Failed);
        Assert.Equal(1, project.Skipped);
        Assert.Equal(12.5, project.Seconds);
        Assert.Equal(["Rules.AreBroken"], project.Failures);
    }

    [Fact]
    public void AFailureCarriesTheFirstLineOfWhatTheTestSaid_SoTheReportShowsWhy()
    {
        var trx = Trx.Replace(
            "<UnitTestResult testName=\"Rules.AreBroken\" outcome=\"Failed\" />",
            "<UnitTestResult testName=\"Rules.AreBroken\" outcome=\"Failed\"><Output><ErrorInfo><Message>Assert.EndsWith() Failure: String end does not match\nString: \"terms#billing?\"</Message><StackTrace>at x</StackTrace></ErrorInfo></Output></UnitTestResult>",
            StringComparison.Ordinal);

        var project = TestResults.ParseTrx("Core.Tests", trx);

        Assert.Equal(["Rules.AreBroken - Assert.EndsWith() Failure: String end does not match"], project.Failures);
    }

    [Fact]
    public void ALongMessageIsCutShort()
    {
        var trx = Trx.Replace(
            "<UnitTestResult testName=\"Rules.AreBroken\" outcome=\"Failed\" />",
            $"<UnitTestResult testName=\"Rules.AreBroken\" outcome=\"Failed\"><Output><ErrorInfo><Message>{new string('x', 500)}</Message></ErrorInfo></Output></UnitTestResult>",
            StringComparison.Ordinal);

        var failure = Assert.Single(TestResults.ParseTrx("Core.Tests", trx).Failures);

        Assert.EndsWith("...", failure, StringComparison.Ordinal);
        Assert.True(failure.Length < 330);
    }

    [Fact]
    public void ALineCountsAsRunIfAnyProjectRanIt()
    {
        var files = new Dictionary<string, Dictionary<int, int>>(StringComparer.OrdinalIgnoreCase);

        TestResults.MergeCoverage(files, Cobertura(@"C:\site\src\Core\A.cs", (1, 0), (2, 5), (3, 0)));
        TestResults.MergeCoverage(files, Cobertura(@"C:\site\src\Core\A.cs", (1, 2), (2, 0), (4, 0)));

        var groups = TestResults.Summarize(files, @"C:\site", new ToolConfig());

        var core = Assert.Single(groups);
        Assert.Equal("src/Core", core.Name);
        Assert.Equal(4, core.Lines);     // lines 1 to 4
        Assert.Equal(2, core.Covered);   // 1 (second project) and 2 (first project)
    }

    [Fact]
    public void PathsRelativeToTheRepositoryRootAreResolvedAgainstIt()
    {
        var files = new Dictionary<string, Dictionary<int, int>>(StringComparer.OrdinalIgnoreCase);
        TestResults.MergeCoverage(files, Cobertura(@"src\Core\Relative.cs", (1, 1), (2, 0)));
        TestResults.MergeCoverage(files, Cobertura(@"tests\Core.Tests\SomeTests.cs", (1, 1)));

        var groups = TestResults.Summarize(files, @"C:\site", new ToolConfig());

        var core = Assert.Single(groups);
        Assert.Equal("src/Core", core.Name);
        Assert.Equal(2, core.Lines);
    }

    [Fact]
    public void PathsRelativeToTheProjectFolder_AreResolvedThroughTheCoberturaSource()
    {
        // The coverage collector lists "Accounts/User.cs" relative to the project folder named in <source>, not to the repository root.
        var root = Path.Combine(Path.GetTempPath(), "quality-cov-" + Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "src", "Core");
        Directory.CreateDirectory(Path.Combine(project, "Accounts"));
        File.WriteAllText(Path.Combine(project, "Accounts", "User.cs"), "class User { }");
        try
        {
            var xml = "<coverage><sources><source>" + project.Replace('\\', '/') + "/</source></sources><packages><package><classes>"
                + "<class filename=\"Accounts/User.cs\"><lines><line number=\"1\" hits=\"3\"/><line number=\"2\" hits=\"0\"/></lines></class>"
                + "<class filename=\"obj/Debug/Generated.g.cs\"><lines><line number=\"1\" hits=\"1\"/></lines></class>"
                + "</classes></package></packages></coverage>";
            var files = new Dictionary<string, Dictionary<int, int>>(StringComparer.OrdinalIgnoreCase);

            TestResults.MergeCoverage(files, xml);
            var group = Assert.Single(TestResults.Summarize(files, root, new ToolConfig()));

            Assert.Equal("src/Core", group.Name);
            Assert.Equal((1, 2), (group.Covered, group.Lines));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void OnlyOurOwnSourceCounts_NotGeneratedCodeTestsOrSkippedFolders()
    {
        var files = new Dictionary<string, Dictionary<int, int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in new[]
                 {
                     @"C:\site\src\Core\Keep.cs",
                     @"C:\site\src\Core\obj\Debug\Generated.cs",
                     @"C:\site\src\Web\Page.razor.g.cs",
                     @"C:\site\tests\Core.Tests\SomeTests.cs",
                     @"C:\site\tools\Shared\Skipped.cs",
                     @"D:\elsewhere\src\Core\Other.cs",
                 })
        {
            TestResults.MergeCoverage(files, Cobertura(file, (1, 1)));
        }

        var config = new ToolConfig { SkipFolders = ["/tools/Shared/"] };

        var groups = TestResults.Summarize(files, @"C:\site", config);

        Assert.Equal(["src/Core"], groups.Select(g => g.Name));
        Assert.Equal(1, groups[0].Lines);
    }

    [Fact]
    public void ReadingTheResultsFolderCombinesEveryProject()
    {
        foreach (var name in new[] { "A.Tests", "B.Tests" })
        {
            var folder = Path.Combine(TestResults.ResultsFolder(_root), name, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(TestResults.ResultsFolder(_root), name, "results.trx"), Trx);
            File.WriteAllText(Path.Combine(folder, "coverage.cobertura.xml"), Cobertura(_root + "/src/Core/" + name + ".cs", (1, 1), (2, 0)));
        }

        var summary = TestResults.Read(_root, new ToolConfig())!;

        Assert.Equal(2, summary.Projects.Count);
        Assert.Equal(6, summary.Total);
        Assert.Equal(2, summary.Failed);
        Assert.Equal(4, summary.CoverableLines);
        Assert.Equal(0.5, summary.CoverageRate);
    }

    [Fact]
    public void WithNoResultsThereIsNoSummary()
    {
        Assert.Null(TestResults.Read(_root, new ToolConfig()));
    }

    [Fact]
    public void BrowserTestsAreLeftOutOfTheDefaultRunButCanBeIncluded()
    {
        foreach (var name in new[] { "Core.Tests", "Site.Browser.Tests" })
        {
            Directory.CreateDirectory(Path.Combine(_root, "tests", name));
            File.WriteAllText(Path.Combine(_root, "tests", name, name + ".csproj"), "<Project />");
        }

        var normal = TestRunner.Projects(_root, new ToolConfig(), includeBrowser: false);
        var all = TestRunner.Projects(_root, new ToolConfig(), includeBrowser: true);

        Assert.Equal(["Core.Tests"], normal.Select(p => Path.GetFileNameWithoutExtension(p)));
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void TheTestNumbersInASnapshotCompareByTotalFailuresAndCoverage()
    {
        var a = new TestCounts(100, 0, 0.5);

        Assert.True(a.SameAs(new TestCounts(100, 0, 0.50001)));
        Assert.False(a.SameAs(new TestCounts(101, 0, 0.5)));
        Assert.False(a.SameAs(new TestCounts(100, 1, 0.5)));
        Assert.False(a.SameAs(new TestCounts(100, 0, null)));
    }
}
