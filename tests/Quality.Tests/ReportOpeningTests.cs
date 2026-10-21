/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;
using CodeQuality.Quality;

namespace CodeQuality.Quality.Tests;

public sealed class ReportOpeningTests
{
    [Theory]
    [InlineData("never", 0, false)]
    [InlineData("never", 1, false)]
    [InlineData(null, 1, false)]
    [InlineData("always", 0, true)]
    [InlineData("ALWAYS", 1, true)]
    [InlineData("onFindings", 0, false)]
    [InlineData("onFindings", 1, true)]
    [InlineData("onFindings", 2, true)]
    [InlineData("something-else", 1, false)]
    public void TheSettingDecidesWhetherARunOpensTheReport(string? mode, int exitCode, bool expected)
    {
        Assert.Equal(expected, HtmlReport.ShouldOpen(mode, exitCode, openFlag: false, noOpenFlag: false));
    }

    [Fact]
    public void TheLivePage_AsksAnOlderLiveTabToReload_AndClosesItself()
    {
        Assert.Contains("new BroadcastChannel('quality-live')", LivePage.Html);
        Assert.Contains("window.close()", LivePage.Html);
        Assert.Contains("type: 'reload'", LivePage.Html);
    }

    [Fact]
    public void TheLiveViewUsesTheReuseWindow_UnlessOpenIsAsked()
    {
        var folder = Path.Combine(Path.GetTempPath(), "cq-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var page = Path.Combine(folder, "index.html");
        var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

        Assert.True(HtmlReport.ShouldOpenNow(page, force: false, now));                   // the first run opens a tab
        Assert.False(HtmlReport.ShouldOpenNow(page, force: false, now.AddMinutes(30)));   // the next one reuses it
        Assert.True(HtmlReport.ShouldOpenNow(page, force: true, now.AddMinutes(31)));     // --open opens another
        Assert.True(HtmlReport.ShouldOpenNow(page, force: false, now.AddHours(7)));       // an old one is not assumed to be open
    }

    [Fact]
    public void TheOpenFlagAlwaysOpens_AndNoOpenSwitchesTheSettingOff()
    {
        Assert.True(HtmlReport.ShouldOpen("never", 0, openFlag: true, noOpenFlag: false));
        Assert.True(HtmlReport.ShouldOpen("never", 0, openFlag: true, noOpenFlag: true));
        Assert.False(HtmlReport.ShouldOpen("always", 1, openFlag: false, noOpenFlag: true));
    }

    [Fact]
    public void TheSettingIsReadFromQualityJson_AndDefaultsToNever()
    {
        var root = Path.Combine(Path.GetTempPath(), "cq-open-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "quality"));
        try
        {
            Assert.Equal("never", ToolConfig.Load(root).OpenReport);

            File.WriteAllText(ToolConfig.PathFor(root), "{ \"openReport\": \"always\" }");
            Assert.Equal("always", ToolConfig.Load(root).OpenReport);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AnAutomaticOpen_LeavesARecentlyOpenedWindowAlone_AndAskingToOpenAlwaysOpens()
    {
        var folder = Path.Combine(Path.GetTempPath(), "cq-reuse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "quality-latest.html");
            var now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

            Assert.True(HtmlReport.ShouldOpenNow(path, force: false, now));                                        // the first one opens
            Assert.False(HtmlReport.ShouldOpenNow(path, force: false, now.AddMinutes(20)));                       // the page is refreshing itself
            Assert.True(HtmlReport.ShouldOpenNow(path, force: true, now.AddMinutes(25)));                         // --open opens one regardless
            Assert.True(HtmlReport.ShouldOpenNow(path, force: false, now.AddMinutes(25) + HtmlReport.ReuseWindowFor));   // long enough ago to be gone
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
