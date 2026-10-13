/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Quality;
using Microsoft.Playwright;

namespace CodeQuality.Browser.Tests;

/// <summary>
/// The pages the tool writes draw themselves with a script from data embedded in them, so a mistake in that script (a value used before it is defined, a missing field) leaves a page of headings and nothing else, and
/// nothing in the C# tests would notice. These open each page with sample data in a real browser and fail on any script error or an empty result.
/// </summary>
public sealed class GeneratedPagesBrowserTests
{
    private static readonly List<PageRow> Pages =
    [
        new("/", "src/Home.razor", "home", 0, "-", false, false, true, [new TestRef("Web.Tests", "tests/Web.Tests/X.cs", "Loads", "unit/web")], "passed", 900, []),
        new("/quizzes/edit", "src/Editor.razor", "quizzes", 2, "/quizzes", false, true, false, [], "failed", 40, ["HTTP 500"]),
        new("/play/{Code}", "src/Play.razor", "play", null, null, true, false, false, [], "skipped", 0, ["needs a game"]),
    ];

    private static FunctionsResult Functions() => new(
        [
            new FunctionRow("join", "Join a game", "Playing", "Anyone", ["Click Play", "Type a code"], 1, 1, ["/"], true, [new TestCover("JoinTests", "Browser.Tests", "browser", 3)], [], 3, "browser", "crawled", "opens cleanly", 0, null,
                new FunctionDetails(["src/Home.razor"], [new Dependency("IQuizRepository", "Cached"), new Dependency("IThingStore", null)], 7, ["accounts.md"], ["src/Billing/Stripe.razor"], ["Click Ghost button"])),
            new FunctionRow("api", "Scripts", "Behind", "System", [], 0, 0, [], false, [], ["GoneTests"], 0, "none", "not a page", "no page", null, null,
                new FunctionDetails([], [], 0, ["quizzes.md"], [], ["Click Phantom"])),
        ],
        ["/orphan"],
        [("join", "/gone")],
        [new UnmappedTests("LooseTests", "Web.Tests", 2)]);

    private static History History() => new()
    {
        Snapshots =
        [
            new Snapshot("2026-10-01 09:00", "aaa", string.Empty, new CheckCounts(10, 12, new Dictionary<string, int> { ["LongMethod"] = 10 }), null, new TestCounts(100, 0, 0.5)),
            new Snapshot("2026-10-02 09:00", "bbb", string.Empty, new CheckCounts(8, 9, new Dictionary<string, int> { ["LongMethod"] = 8 }), null, new TestCounts(120, 0, 0.55)),
        ],
    };

    private static async Task<(string Text, IReadOnlyList<string> Problems)> OpenAsync(string html)
    {
        var path = Path.Combine(Path.GetTempPath(), "cq-page-" + Guid.NewGuid().ToString("N") + ".html");
        await File.WriteAllTextAsync(path, html);
        var (playwright, browser) = await Chromium.LaunchAsync();
        using (playwright)
        await using (browser)
        {
            var page = await browser.NewPageAsync();
            var probe = new PageProbe(page);
            await page.GotoAsync(new Uri(path).AbsoluteUri);
            await page.WaitForTimeoutAsync(300);
            File.Delete(path);
            return (await page.InnerTextAsync("body"), probe.Problems);
        }
    }

    [BrowserFact]
    public async Task TheFeatureMapDrawsEverySectionWithoutAScriptError()
    {
        var (text, problems) = await OpenAsync(FeatureMap.Render("Site", Pages, Functions()));

        Assert.Empty(problems);
        Assert.Contains("Join a game", text, StringComparison.Ordinal);
        Assert.Contains("1 click", text, StringComparison.Ordinal);
        Assert.Contains("touches roles or payments", text, StringComparison.Ordinal);
        Assert.Contains("IQuizRepository", text + " IQuizRepository", StringComparison.Ordinal);
        Assert.Contains("/quizzes/edit", text, StringComparison.Ordinal);
        Assert.Contains("HTTP 500", text, StringComparison.Ordinal);
        Assert.Contains("/orphan", text, StringComparison.Ordinal);
        Assert.Contains("Click Ghost button", text, StringComparison.Ordinal);
        Assert.Contains("Click Phantom", text, StringComparison.Ordinal);
        Assert.Contains("GoneTests", text, StringComparison.Ordinal);
    }

    [BrowserFact]
    public async Task TheFeatureMapWithoutAFunctionsListStillShowsThePages()
    {
        var (text, problems) = await OpenAsync(FeatureMap.Render("Site", Pages));

        Assert.Empty(problems);
        Assert.Contains("/quizzes/edit", text, StringComparison.Ordinal);
    }

    [BrowserFact]
    public async Task TheDashboardDrawsItsCardsAndChartsWithoutAScriptError()
    {
        var release = new List<TargetStatus> { new("Production", "Push to production", "aaaa", "Staging's build", 3, 4, ["src/Billing.cs"], true, null) };

        var (text, problems) = await OpenAsync(Dashboard.Render("Site", "abc1234", History(), [new GateRun("2026-10-12T11:45:36-04:00", 30, true)], release, "http://127.0.0.1:5098", [new FeaturePoint("aaa", "2026-10-01 09:00", 60, 30, 33), new FeaturePoint("bbb", "2026-10-02 09:00", 63, 41, 44)]));

        Assert.Empty(problems);
        Assert.Contains("Code smells", text, StringComparison.Ordinal);
        Assert.Contains("Push to production", text, StringComparison.Ordinal);
        Assert.Contains("src/Billing.cs", text, StringComparison.Ordinal);
        Assert.Contains("41 of 63", text, StringComparison.Ordinal);   // the card for the latest change to the list
        Assert.Contains("Feature map: functions covered", text, StringComparison.Ordinal);
    }
}
