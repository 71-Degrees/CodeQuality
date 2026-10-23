/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Microsoft.Playwright;

namespace CodeQuality.Quality;

/// <summary>
/// The copy of the report that gets shipped to a hosted dashboard. The report draws itself with a script, but a hosted copy is served under a policy that allows no script, so the page would be empty. This
/// opens the finished report in a headless Chromium, lets the script draw it, and saves what is then in the DOM - with the script and the controls that need it taken out - as plain HTML.
/// </summary>
internal static class StaticSnapshot
{
    public const string FileName = "quality-latest-static.html";

    // Nothing here runs in the saved page: scripts go, and so do the live-refresh row and the filter controls, which would be dead without them.
    private const string Strip = "document.querySelectorAll('script, #live, .controls').forEach(e => e.remove());";

    /// <summary>Writes <c>quality-latest-static.html</c> beside <paramref name="reportPath"/>. Best effort: returns null (with the reason on the console) when Chromium is not there, so a run never fails over the shipped copy.</summary>
    public static async Task<string?> TryWriteAsync(string reportPath)
    {
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            var page = await browser.NewPageAsync();
            await page.GotoAsync(new Uri(reportPath).AbsoluteUri, new PageGotoOptions { WaitUntil = WaitUntilState.Load });
            await page.WaitForSelectorAsync("#hero .big");
            await page.EvaluateAsync(Strip);
            var target = Path.Combine(Path.GetDirectoryName(reportPath)!, FileName);
            await File.WriteAllTextAsync(target, await page.ContentAsync());
            return target;
        }
        catch (Exception e) when (e is PlaywrightException or TimeoutException)
        {
            Console.WriteLine($"  (the static copy of the report for the dashboard was not made: {e.Message.Split('\n')[0]})");
            return null;
        }
    }
}
