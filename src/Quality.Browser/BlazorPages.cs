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
    /// <summary>
    /// Runs in every page before its own scripts and notes when the page's live connection (a WebSocket) opens and when it first hears from the server. "The Blazor runtime has loaded" comes well before that, and
    /// anything typed or clicked in between is lost. A page with no live connection (a static page) never opens one, which <see cref="WaitUntilLiveAsync"/> allows for.
    /// </summary>
    public const string WatchLiveConnectionScript = """
        (() => {
            window.__live = { opened: 0, messages: 0 };
            const Original = window.WebSocket;
            window.WebSocket = function (...args) {
                const socket = new Original(...args);
                socket.addEventListener('open', () => window.__live.opened++);
                socket.addEventListener('message', () => window.__live.messages++);
                return socket;
            };
            window.WebSocket.prototype = Original.prototype;
            Object.assign(window.WebSocket, Original);
        })();
        """;

    /// <summary>Goes to the address and waits until the page is interactive: the Blazor runtime has started and no error or reconnect bar is showing.</summary>
    public static async Task GotoLiveAsync(this IPage page, string url, float timeoutMilliseconds = 30_000)
    {
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = timeoutMilliseconds }).ConfigureAwait(false);
        await page.WaitUntilLiveAsync(timeoutMilliseconds).ConfigureAwait(false);
    }

    /// <summary>Waits until the page in front is interactive (the runtime has started, no error or reconnect bar). Call it after anything that loads a new page, such as a click on a link: the click returns before the new page has begun to load, and typing into a page that is not live yet is lost.</summary>
    public static async Task WaitUntilLiveAsync(this IPage page, float timeoutMilliseconds = 30_000)
    {
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new PageWaitForLoadStateOptions { Timeout = timeoutMilliseconds }).ConfigureAwait(false);
        await page.WaitForFunctionAsync("() => window.Blazor !== undefined", null, new PageWaitForFunctionOptions { Timeout = timeoutMilliseconds }).ConfigureAwait(false);
        await WaitForLiveConnectionAsync(page).ConfigureAwait(false);
        await page.WaitForFunctionAsync(
            "() => { const bar = document.querySelector('#blazor-error-ui'); const reconnect = document.querySelector('.components-reconnect-show'); return (!bar || getComputedStyle(bar).display === 'none') && !reconnect; }",
            null,
            new PageWaitForFunctionOptions { Timeout = timeoutMilliseconds }).ConfigureAwait(false);
    }

    /// <summary>A page that is going to be live opens its connection within moments of loading; once it has, it is ready when the server has spoken. A page that opens none within the grace period is a static one.</summary>
    private static async Task WaitForLiveConnectionAsync(IPage page)
    {
        try
        {
            await page.WaitForFunctionAsync("() => window.__live && window.__live.opened > 0", null, new PageWaitForFunctionOptions { Timeout = 2_000 }).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return;   // no live connection on this page
        }

        await page.WaitForFunctionAsync("() => window.__live.messages > 0", null, new PageWaitForFunctionOptions { Timeout = 15_000 }).ConfigureAwait(false);
    }

    /// <summary>Whether the page is showing Blazor's "an unhandled error has occurred" bar, which means the circuit crashed.</summary>
    public static Task<bool> ShowsBlazorErrorAsync(this IPage page) =>
        page.EvaluateAsync<bool>("() => { const bar = document.querySelector('#blazor-error-ui'); return !!bar && getComputedStyle(bar).display !== 'none'; }");
}
