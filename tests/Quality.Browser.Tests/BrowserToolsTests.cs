/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Net;
using CodeQuality.Browser;
using Microsoft.Playwright;

namespace CodeQuality.Browser.Tests;

public sealed class BrowserToolsTests
{
    [Theory]
    [InlineData("error", true)]
    [InlineData("ERROR", true)]
    [InlineData("warning", false)]
    [InlineData("log", false)]
    public void OnlyConsoleErrorsAreProblems(string type, bool expected) =>
        Assert.Equal(expected, ProbeFilter.IsConsoleProblem(type));

    [Theory]
    [InlineData(200, false)]
    [InlineData(401, false)]
    [InlineData(404, false)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    public void OnlyServerFailuresAreProblems(int status, bool expected) =>
        Assert.Equal(expected, ProbeFilter.IsResponseProblem(status));

    [Fact]
    public void IgnoredMessagesAreLeftOutByPattern()
    {
        var filter = new ProbeFilter().Ignore("known third-party noise");

        Assert.True(filter.IsIgnored("console error: Known Third-Party Noise from vendor.js"));
        Assert.False(filter.IsIgnored("console error: real problem"));
    }

    [Fact]
    public void AFreePortIsOneNobodyIsListeningOn()
    {
        var port = SiteHost.FreePort();

        Assert.InRange(port, 1024, 65535);
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
        listener.Start();   // would throw if it were taken
    }

    [Fact]
    public void BrowserDataFollowsTheOutputRootWhenOneIsNamed()
    {
        var elsewhere = Path.GetTempPath();

        Assert.Equal("/repo", BrowserData.OutputRoot("/repo", null));
        Assert.Equal(elsewhere, BrowserData.OutputRoot("/repo", elsewhere));
        Assert.Equal("/repo", BrowserData.OutputRoot("/repo", Path.Combine(elsewhere, "cq-missing-" + Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public async Task WaitingForTheSiteReturnsOnceItAnswers()
    {
        var port = SiteHost.FreePort();
        var url = $"http://127.0.0.1:{port}/healthz/";
        using var server = new HttpListener();
        server.Prefixes.Add(url);
        var calls = 0;
        server.Start();
        var serving = Task.Run(async () =>
        {
            while (server.IsListening)
            {
                var context = await server.GetContextAsync();
                context.Response.StatusCode = ++calls < 3 ? 503 : 200;   // not ready for the first two asks
                context.Response.Close();
            }
        });

        await SiteHost.WaitUntilReadyAsync(url, TimeSpan.FromSeconds(20), () => false);

        Assert.True(calls >= 3);
        server.Stop();
        await Task.WhenAny(serving, Task.Delay(1000));
    }

    [Fact]
    public async Task WaitingForTheSiteFailsFastWhenItsProcessHasDied()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => SiteHost.WaitUntilReadyAsync("http://127.0.0.1:1/healthz", TimeSpan.FromSeconds(20), () => true));

        Assert.Contains("stopped before it became ready", ex.Message);
    }

    [Fact]
    public async Task WaitingForTheSiteGivesUpAfterTheTimeout()
    {
        await Assert.ThrowsAsync<TimeoutException>(() => SiteHost.WaitUntilReadyAsync("http://127.0.0.1:1/healthz", TimeSpan.FromSeconds(1), () => false));
    }

    [Fact]
    public void TheRunLogAppendsOneLinePerRecord_AndNeverRewritesTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "cq-runlog-" + Guid.NewGuid().ToString("N"), "runs.jsonl");
        var log = new BrowserRunLog(path);

        log.Append("first", TimeSpan.FromMilliseconds(1500), 0);
        log.Append("second", TimeSpan.FromMilliseconds(40), 2, "with a note");

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.Contains("\"name\":\"first\"", lines[0]);
        Assert.Contains("\"milliseconds\":1500", lines[0]);
        Assert.Contains("\"problems\":2", lines[1]);
        Assert.Contains("with a note", lines[1]);
    }

    // ---- with a real browser (skipped where Chromium is not installed) ----

    private static async Task<(IPlaywright Playwright, IBrowser Browser, IPage Page)> OpenAsync()
    {
        var (playwright, browser) = await Chromium.LaunchAsync();
        return (playwright, browser, await browser.NewPageAsync());
    }

    [BrowserFact]
    public async Task TheProbeCatchesAConsoleErrorAndAnUncaughtError_ButNotALog()
    {
        var (playwright, browser, page) = await OpenAsync();
        using (playwright)
        await using (browser)
        {
            var probe = new PageProbe(page);

            await page.SetContentAsync("<script>console.log('fine'); console.warn('also fine'); console.error('boom'); setTimeout(() => { throw new Error('later'); }, 0);</script>");
            await page.WaitForTimeoutAsync(200);

            Assert.Equal(2, probe.Problems.Count);
            Assert.Contains(probe.Problems, p => p.Contains("console error: boom", StringComparison.Ordinal));
            Assert.Contains(probe.Problems, p => p.Contains("later", StringComparison.Ordinal));
            Assert.Throws<BrowserProblemException>(probe.AssertClean);

            probe.Clear();
            probe.AssertClean();
        }
    }

    [BrowserFact]
    public async Task ABlazorPageIsLiveOnlyOnceTheRuntimeHasStartedAndNoErrorBarShows()
    {
        var (playwright, browser, page) = await OpenAsync();
        using (playwright)
        await using (browser)
        {
            await page.SetContentAsync("<div id='blazor-error-ui' style='display:none'>An unhandled error has occurred.</div><script>window.Blazor = {};</script>");
            Assert.False(await page.ShowsBlazorErrorAsync());

            await page.EvaluateAsync("() => { document.getElementById('blazor-error-ui').style.display = 'block'; }");
            Assert.True(await page.ShowsBlazorErrorAsync());
        }
    }
}
