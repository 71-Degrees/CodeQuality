/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Microsoft.Playwright;

namespace CodeQuality.Browser;

/// <summary>Helpers for pages on a Blazor Server site, where "the page loaded" is not enough: the page is only live once its circuit is up, and a crashed circuit shows the error bar instead of failing the request.</summary>
public static class BlazorPages
{
    /// <summary>Goes to the address and waits until the page is interactive: the Blazor runtime has started and no error or reconnect bar is showing.</summary>
    public static async Task GotoLiveAsync(this IPage page, string url, float timeoutMilliseconds = 30_000)
    {
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = timeoutMilliseconds }).ConfigureAwait(false);
        await page.WaitForFunctionAsync("() => window.Blazor !== undefined", null, new PageWaitForFunctionOptions { Timeout = timeoutMilliseconds }).ConfigureAwait(false);
        await page.WaitForFunctionAsync(
            "() => { const bar = document.querySelector('#blazor-error-ui'); const reconnect = document.querySelector('.components-reconnect-show'); return (!bar || getComputedStyle(bar).display === 'none') && !reconnect; }",
            null,
            new PageWaitForFunctionOptions { Timeout = timeoutMilliseconds }).ConfigureAwait(false);
    }

    /// <summary>Whether the page is showing Blazor's "an unhandled error has occurred" bar, which means the circuit crashed.</summary>
    public static Task<bool> ShowsBlazorErrorAsync(this IPage page) =>
        page.EvaluateAsync<bool>("() => { const bar = document.querySelector('#blazor-error-ui'); return !!bar && getComputedStyle(bar).display !== 'none'; }");
}
