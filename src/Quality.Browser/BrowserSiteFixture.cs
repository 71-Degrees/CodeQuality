/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Microsoft.Playwright;
using Xunit;

namespace CodeQuality.Browser;

/// <summary>
/// The base of a site's browser-test fixture: starts the site once for all the tests that share it, launches one Chromium, and hands each test its own page (its own browser context, phone-sized by default) with a
/// <see cref="PageProbe"/> already listening. The site's own subclass says only how its site starts (<see cref="Options"/>) and where its run log goes.
/// </summary>
public abstract class BrowserSiteFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    /// <summary>How to start the site: its project, storage, settings.</summary>
    protected abstract SiteHostOptions Options { get; }

    /// <summary>The file that records each scenario's timing (appended to, never cleaned up).</summary>
    protected abstract string RunLogPath { get; }

    public SiteHost Site { get; private set; } = null!;

    public BrowserRunLog Log { get; private set; } = null!;

    public string BaseUrl => Site.BaseUrl;

    public async Task InitializeAsync()
    {
        Log = new BrowserRunLog(RunLogPath);
        Site = await SiteHost.StartAsync(Options).ConfigureAwait(false);
        Log.Append("site started", Site.StartedIn, 0);
        (_playwright, _browser) = await Chromium.LaunchAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// A fresh page in its own browser context (cookies and storage are not shared with any other test unless <paramref name="storageStatePath"/> says so: a file saved by <see cref="SaveStorageStateAsync"/>, which lets many tests
    /// start already signed in after one real sign-in), with a probe listening.
    /// </summary>
    public async Task<BrowserPage> NewPageAsync(int width = 390, int height = 844, string? storageStatePath = null)
    {
        var context = await _browser!.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = width, Height = height }, BaseURL = BaseUrl, StorageStatePath = storageStatePath }).ConfigureAwait(false);
        var page = await context.NewPageAsync().ConfigureAwait(false);
        return new BrowserPage(context, page, new PageProbe(page), Log);
    }

    /// <summary>Saves the page's cookies and storage (its signed-in state) to a file other pages can start from.</summary>
    public static Task SaveStorageStateAsync(BrowserPage page, string path) => page.Context.StorageStateAsync(new BrowserContextStorageStateOptions { Path = path });

    public async Task DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync().ConfigureAwait(false);
        }

        _playwright?.Dispose();
        if (Site is not null)
        {
            await Site.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>One test's page, its browser context, and the probe that fails the test on a console error, a page error or a server error.</summary>
public sealed class BrowserPage : IAsyncDisposable
{
    private readonly BrowserRunLog _log;

    public BrowserPage(IBrowserContext context, IPage page, PageProbe probe, BrowserRunLog log)
    {
        Context = context;
        Page = page;
        Probe = probe;
        _log = log;
    }

    public IPage Page { get; }

    public IBrowserContext Context { get; }

    public PageProbe Probe { get; }

    /// <summary>Runs the scenario, records how long it took and how many problems the page had, and fails if there were any.</summary>
    public Task RunAsync(string name, Func<IPage, Task> scenario) => _log.TimeAsync(name, Probe, () => scenario(Page));

    public ValueTask DisposeAsync() => Context.DisposeAsync();
}
