/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Quality.Tests;

/// <summary>What a failed `dotnet test` run says about itself: the failing tests by name, or the last lines of a run that never reported.</summary>
public class TestRunOutputTests
{
    private const string Failing = """
        [xUnit.net 00:00:16.13]     Site.Tests.HostTests.StartIsInReach [FAIL]
          Failed Site.Tests.HostTests.StartIsInReach [30 s]
          Error Message:
           Microsoft.Playwright.PlaywrightException : Locator expected to be visible
          Stack Trace:
             at X.Y()
        [xUnit.net 00:00:20.00]     Site.Tests.SubscriptionTests.Login [FAIL]
        Failed!  - Failed:     2, Passed:    91, Skipped:     0, Total:    93, Duration: 7 m 4 s - Site.Tests.dll (net10.0)
        """;

    [Fact]
    public void AFailingTestIsNamedOnce_WithTheFirstLineOfItsMessage()
    {
        var failed = TestRunOutput.FailedTests(Failing);

        Assert.Equal(2, failed.Count);
        Assert.Equal("Site.Tests.HostTests.StartIsInReach - Microsoft.Playwright.PlaywrightException : Locator expected to be visible", failed[0]);
        Assert.Equal("Site.Tests.SubscriptionTests.Login", failed[1]);
    }

    [Fact]
    public void ThePassedAndFailedLine_IsTheSummary_AndARunWithoutOneHasNone()
    {
        Assert.StartsWith("Failed!", TestRunOutput.SummaryLine(Failing));
        Assert.Null(TestRunOutput.SummaryLine("error MSB3027: Could not copy Ship.dll\nBuild FAILED."));
    }

    [Fact]
    public void ARunThatNeverReported_ShowsItsLastNonEmptyLines()
    {
        var output = string.Join('\n', Enumerable.Range(1, 20).Select(i => i % 5 == 0 ? string.Empty : $"line {i}  "));

        var last = TestRunOutput.LastLines(output, 3);

        Assert.Equal(["line 17", "line 18", "line 19"], last);
    }

    [Fact]
    public void ThereAreNoMoreThanTenNames_AndAPassingRunNamesNone()
    {
        var many = string.Join('\n', Enumerable.Range(1, 30).Select(i => $"[xUnit.net 00:00:01]     Ns.C.T{i} [FAIL]"));

        Assert.Equal(10, TestRunOutput.FailedTests(many).Count);
        Assert.Empty(TestRunOutput.FailedTests("Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5"));
    }
}
