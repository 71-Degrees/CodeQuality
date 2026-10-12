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
}
