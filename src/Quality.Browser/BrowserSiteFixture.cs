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
    // A transparent 1x1 picture, the answer a blocked image request gets (so a page that asked for a map tile shows a blank one and logs no error).
    private static readonly byte[] TransparentPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _blockedHosts = new(StringComparer.OrdinalIgnoreCase);
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    /// <summary>How to start the site: its project, storage, settings.</summary>
    protected abstract SiteHostOptions Options { get; }

    /// <summary>
    /// Whether a request to this address must not leave the machine (a map tile, a font, an avatar on someone else's server). A blocked request is answered with nothing, or a transparent picture for an image, and its host is
    /// written once to the run log, so a test run never reaches an outside service and the log shows what the pages tried to reach. Nothing is blocked by default.
    /// </summary>
    protected virtual bool BlocksRequest(Uri address) => false;

    /// <summary>The file that records each scenario's timing (appended to, never cleaned up).</summary>
    protected abstract string RunLogPath { get; }

    /// <summary>Extra Chromium switches for this site's tests (for example <see cref="Chromium.FakeCameraArguments"/>). None by default.</summary>
    protected virtual IReadOnlyList<string> BrowserArguments => [];

    public SiteHost Site { get; private set; } = null!;

    public BrowserRunLog Log { get; private set; } = null!;

    public string BaseUrl => Site.BaseUrl;

    public async Task InitializeAsync()
    {
        Log = new BrowserRunLog(RunLogPath);
        Site = await SiteHost.StartAsync(Options).ConfigureAwait(false);
        Log.Append("site started", Site.StartedIn, 0);
        (_playwright, _browser) = await Chromium.LaunchAsync(BrowserArguments).ConfigureAwait(false);
        // A journey runs against a site that has just started on a machine that may be busy; the 5 seconds Playwright waits for an expected thing is too tight for the first pages, and a longer wait costs nothing when things are fine.
        Assertions.SetDefaultExpectTimeout(15_000);
    }

    /// <summary>
    /// A fresh page in its own browser context (cookies and storage are not shared with any other test unless <paramref name="storageStatePath"/> says so: a file saved by <see cref="SaveStorageStateAsync"/>, which lets many tests
    /// start already signed in after one real sign-in), with a probe listening.
    /// </summary>
    public Task<BrowserPage> NewPageAsync(int width = 390, int height = 844, string? storageStatePath = null) =>
        NewPageAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = width, Height = height }, StorageStatePath = storageStatePath });

    /// <summary>
    /// A fresh page that behaves like a phone: touch input (so CSS <c>pointer: coarse</c> and touch-only code paths are taken), the mobile viewport, and the camera
    /// permission granted. Pair it with <see cref="Chromium.FakeCameraArguments"/> to drive a page that opens the camera.
    /// </summary>
    public Task<BrowserPage> NewPhonePageAsync(string? storageStatePath = null) =>
        NewPageAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            HasTouch = true,
            IsMobile = true,
            Permissions = ["camera"],
            StorageStatePath = storageStatePath,
        });

    /// <summary>A fresh page from the caller's own context options; the base address, the outside-request blocking and the probe are added here.</summary>
    public async Task<BrowserPage> NewPageAsync(BrowserNewContextOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.BaseURL = BaseUrl;
        var context = await _browser!.NewContextAsync(options).ConfigureAwait(false);
        await context.RouteAsync("**/*", AnswerOrBlockAsync).ConfigureAwait(false);
        await context.AddInitScriptAsync(BlazorPages.WatchLiveConnectionScript).ConfigureAwait(false);
        var page = await context.NewPageAsync().ConfigureAwait(false);
        return new BrowserPage(context, page, new PageProbe(page), Log);
    }

    private async Task AnswerOrBlockAsync(IRoute route)
    {
        if (!Uri.TryCreate(route.Request.Url, UriKind.Absolute, out var address) || !BlocksRequest(address))
        {
            await route.ContinueAsync().ConfigureAwait(false);
            return;
        }

        if (_blockedHosts.TryAdd(address.Host, true))
        {
            Log.Append("blocked outside request", TimeSpan.Zero, 0, address.Host);
        }

        if (route.Request.ResourceType == "image")
        {
            await route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "image/png", BodyBytes = TransparentPng }).ConfigureAwait(false);
        }
        else
        {
            await route.FulfillAsync(new RouteFulfillOptions { Status = 204 }).ConfigureAwait(false);
        }
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
