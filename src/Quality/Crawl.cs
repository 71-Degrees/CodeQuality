/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.Json;
using System.Text.RegularExpressions;
using CodeQuality.Common;
using Microsoft.Playwright;

namespace CodeQuality.Quality;

internal enum Severity { Error, Warning }

internal sealed record CrawlIssue(Severity Severity, string Page, string Text);

internal sealed record CrawlResult(int Pages, IReadOnlyList<CrawlIssue> Issues, IReadOnlyList<string> Visited);

/// <summary>
/// Walks the public site like a visitor would, in a real headless Chromium (Playwright; the same one tools/BrowserLoad uses): starts from the home page and the sitemap, follows
/// same-site links, and for every page records what a person or a search engine would trip over - a broken link, a script error in the console, a request that failed, a missing
/// title or description, a page that scrolls sideways on a phone, an image with no alt text, and accessibility violations found by axe-core. Read-only: it only loads pages and never
/// signs in, posts or follows the account, admin, billing or hosting routes. Phone-sized by default, because that is where players are.
/// </summary>
internal static class Crawler
{
    private const string StaticFiles = @"\.(jpg|jpeg|png|webp|gif|svg|ico|pdf|xml|txt|json|js|css|woff2?)$";

    /// <summary>Paths never visited: static files always, plus whatever the repository's own pattern names (sign-in, admin, anything that changes state).</summary>
    private static Regex SkipPattern(CrawlSettings settings) =>
        new(string.IsNullOrWhiteSpace(settings.SkipPattern) ? StaticFiles : $"{settings.SkipPattern}|{StaticFiles}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static async Task<CrawlResult> RunAsync(string root, string baseUrl, int maxPages, bool desktop, int delayMs, CrawlSettings settings)
    {
        var origin = new Uri(baseUrl);
        var frontier = new Frontier(origin, SkipPattern(settings));
        frontier.Enqueue(origin.GetLeftPart(UriPartial.Authority) + "/");
        foreach (var url in await SitemapAsync(origin))
        {
            frontier.Enqueue(url);
        }

        // The machine-wide copy first (quality install); a repo's own node_modules only while that is not installed.
        var axePath = ScriptTools.AxePath() is { } shared && File.Exists(shared) ? shared : Path.Combine(root, "node_modules", "axe-core", "axe.min.js");
        var haveAxe = File.Exists(axePath);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = desktop ? new ViewportSize { Width = 1280, Height = 900 } : new ViewportSize { Width = 390, Height = 844 },
            UserAgent = settings.UserAgent,
            BypassCSP = haveAxe,   // only so axe-core can be injected; the page itself is the one the visitor gets
        });

        // A target that hides itself from search (staging disallows everything and drops the SEO tags on purpose) is not judged on SEO: those warnings would be about staging, not the site.
        var run = new CrawlRun(context, frontier, desktop, await HiddenFromSearchAsync(origin), haveAxe ? axePath : null);
        var issues = new List<CrawlIssue>();
        while (frontier.Count > 0 && run.Visited.Count < maxPages)
        {
            var path = frontier.Dequeue();
            issues.AddRange((await VisitAsync(run, new Uri(origin, path).ToString(), path)).DistinctBy(i => (i.Severity, i.Text)));
            await Task.Delay(delayMs);
        }

        foreach (var (title, pages) in run.Titles.Where(t => t.Value.Count > 1))
        {
            issues.Add(new CrawlIssue(Severity.Warning, pages[0], $"The same <title> \"{Clip(title)}\" is used on {pages.Count} pages ({string.Join(", ", pages.Take(4))})."));
        }

        issues.AddRange(await ExternalAsync(frontier.External.Take(40).ToList(), settings));
        if (frontier.Count > 0)
        {
            issues.Add(new CrawlIssue(Severity.Warning, "(crawl)", $"Stopped at {maxPages} pages with {frontier.Count} more found; raise --max-pages to see them."));
        }

        return new CrawlResult(run.Visited.Count, issues, run.Visited);
    }

    /// <summary>The pages still to visit and the external links seen so far.</summary>
    private sealed class Frontier(Uri origin, Regex skip)
    {
        private readonly Queue<string> _queue = new();
        private readonly HashSet<string> _queued = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> External { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int Count => _queue.Count;

        public string Dequeue() => _queue.Dequeue();

        public void Enqueue(string href)
        {
            if (!Uri.TryCreate(href, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                return;
            }

            if (!uri.Host.Equals(origin.Host, StringComparison.OrdinalIgnoreCase))
            {
                External.Add(uri.GetLeftPart(UriPartial.Path));
                return;
            }

            var path = uri.AbsolutePath.TrimEnd('/');
            path = path.Length == 0 ? "/" : path;
            if (!skip.IsMatch(path) && _queued.Add(path))
            {
                _queue.Enqueue(path);
            }
        }
    }

    /// <summary>What one crawl carries from page to page: the browser context, the frontier, the viewport kind, whether SEO is judged, axe-core's location (null: not installed), the pages visited and the titles seen.</summary>
    private sealed record CrawlRun(IBrowserContext Context, Frontier Frontier, bool Desktop, bool HiddenFromSearch, string? AxePath)
    {
        public List<string> Visited { get; } = new();

        public Dictionary<string, List<string>> Titles { get; } = new();
    }

    /// <summary>Loads one page, reads what a visitor or a search engine would trip over, queues the links it finds, and returns the problems.</summary>
    private static async Task<List<CrawlIssue>> VisitAsync(CrawlRun run, string url, string path)
    {
        var page = await run.Context.NewPageAsync();
        var pageIssues = new List<CrawlIssue>();
        WatchPage(page, path, pageIssues);
        try
        {
            var started = DateTime.UtcNow;
            var response = await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
            await page.WaitForTimeoutAsync(700);   // let the page's own scripts settle (the live connection never goes idle, so "network idle" never comes)
            var ms = (int)(DateTime.UtcNow - started).TotalMilliseconds;
            run.Visited.Add(path);
            if (response is null || response.Status >= 400)
            {
                pageIssues.Add(new CrawlIssue(Severity.Error, path, $"The page answered {response?.Status.ToString() ?? "nothing"}."));
            }
            else
            {
                await ReadPageAsync(run, page, path, ms, pageIssues);
            }
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException)
        {
            pageIssues.Add(new CrawlIssue(Severity.Error, path, "Could not load: " + Clip(ex.Message)));
        }
        finally
        {
            await page.CloseAsync();
        }

        return pageIssues;
    }

    private static async Task ReadPageAsync(CrawlRun run, IPage page, string path, int ms, List<CrawlIssue> pageIssues)
    {
        var facts = await page.EvaluateAsync<JsonElement>(Facts);
        foreach (var href in facts.GetProperty("links").EnumerateArray().Select(l => l.GetString()!))
        {
            run.Frontier.Enqueue(href);
        }

        CheckFacts(facts, path, ms, run.Desktop, run.HiddenFromSearch, run.Titles, pageIssues);
        if (run.AxePath is not null)
        {
            await AxeAsync(page, run.AxePath, path, pageIssues);
        }
    }

    /// <summary>Records the console errors, script errors and failed requests of one page as they happen.</summary>
    private static void WatchPage(IPage page, string path, List<CrawlIssue> pageIssues)
    {
    page.Console += (_, m) =>
    {
        if (m.Type == "error")
        {
            pageIssues.Add(new CrawlIssue(Severity.Error, path, "Console error: " + Clip(m.Text)));
        }
    };
    page.PageError += (_, e) => pageIssues.Add(new CrawlIssue(Severity.Error, path, "Script error: " + Clip(e)));
    page.RequestFailed += (_, r) =>
    {
        if (r.Failure is { } failure && !failure.Contains("ERR_ABORTED", StringComparison.Ordinal))
        {
            pageIssues.Add(new CrawlIssue(Severity.Error, path, $"Request failed ({failure}): {Clip(r.Url)}"));
        }
    };
    page.Response += (_, r) =>
    {
        if (r.Status >= 400 && r.Request.ResourceType != "document")
        {
            pageIssues.Add(new CrawlIssue(Severity.Error, path, $"A file the page loads answered {r.Status}: {Clip(r.Url)}"));
        }
    };
    }

    /// <summary>What the page itself says about itself: title, description, headings, image alt text, sideways scrolling, speed.</summary>
    private static void CheckFacts(JsonElement facts, string path, int ms, bool desktop, bool hiddenFromSearch, Dictionary<string, List<string>> titles, List<CrawlIssue> pageIssues)
    {
    var title = facts.GetProperty("title").GetString() ?? string.Empty;
    if (hiddenFromSearch)
    {
        // no SEO judgement here
    }
    else if (title.Length == 0)
    {
        pageIssues.Add(new CrawlIssue(Severity.Warning, path, "No <title>."));
    }
    else
    {
        titles.TryAdd(title, new List<string>());
        titles[title].Add(path);
    }

    if (!hiddenFromSearch && (facts.GetProperty("description").GetString() ?? string.Empty).Length == 0)
    {
        pageIssues.Add(new CrawlIssue(Severity.Warning, path, "No meta description."));
    }

    var h1 = facts.GetProperty("h1").GetInt32();
    if (h1 != 1)
    {
        pageIssues.Add(new CrawlIssue(Severity.Warning, path, h1 == 0 ? "No <h1> heading." : $"{h1} <h1> headings (one is expected)."));
    }

    if (facts.GetProperty("imgNoAlt").GetInt32() is > 0 and var noAlt)
    {
        pageIssues.Add(new CrawlIssue(Severity.Warning, path, $"{noAlt} image(s) with no alt attribute."));
    }

    if (!desktop && facts.GetProperty("sideways").GetBoolean())
    {
        pageIssues.Add(new CrawlIssue(Severity.Warning, path, "Scrolls sideways on a phone-sized screen."));
    }

    if (ms > 3000)
    {
        pageIssues.Add(new CrawlIssue(Severity.Warning, path, $"Slow: {ms} ms to the page's first content."));
    }
    }

    private const string Facts = """
        () => ({
            title: document.title || '',
            description: (document.querySelector('meta[name=description]') || {}).content || '',
            h1: document.querySelectorAll('h1').length,
            imgNoAlt: [...document.querySelectorAll('img')].filter(i => !i.hasAttribute('alt')).length,
            sideways: document.documentElement.scrollWidth > window.innerWidth + 1,
            links: [...document.querySelectorAll('a[href]')].map(a => a.href)
        })
        """;

    private static async Task AxeAsync(IPage page, string axePath, string path, List<CrawlIssue> issues)
    {
        try
        {
            await page.AddScriptTagAsync(new PageAddScriptTagOptions { Path = axePath });
            var violations = await page.EvaluateAsync<JsonElement>(
                "async () => (await axe.run(document, { runOnly: ['wcag2a', 'wcag2aa'] })).violations.map(v => ({ id: v.id, impact: v.impact, help: v.help, nodes: v.nodes.length, where: v.nodes.slice(0, 2).map(n => n.target.join(' ') + (n.any[0] ? ' - ' + n.any[0].message : '')).join(' | ') }))");
            foreach (var v in violations.EnumerateArray())
            {
                var impact = v.GetProperty("impact").GetString();
                var severity = impact is "critical" or "serious" ? Severity.Error : Severity.Warning;
                issues.Add(new CrawlIssue(severity, path, $"Accessibility ({impact}): {v.GetProperty("help").GetString()} [{v.GetProperty("id").GetString()}, {v.GetProperty("nodes").GetInt32()} place(s)] e.g. {Clip(v.GetProperty("where").GetString() ?? string.Empty)}"));
            }
        }
        catch (PlaywrightException ex)
        {
            issues.Add(new CrawlIssue(Severity.Warning, path, "Accessibility check could not run: " + Clip(ex.Message)));
        }
    }

    private static async Task<bool> HiddenFromSearchAsync(Uri origin)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var robots = await http.GetStringAsync(new Uri(origin, "/robots.txt"));
            return Regex.IsMatch(robots, @"^\s*Disallow:\s*/\s*$", RegexOptions.Multiline);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private static async Task<IReadOnlyList<string>> SitemapAsync(Uri origin)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var xml = await http.GetStringAsync(new Uri(origin, "/sitemap.xml"));
            // The sitemap names the production address; read its paths against the site being crawled, so staging and local work too.
            return Regex.Matches(xml, "<loc>(.*?)</loc>").Select(m => new Uri(origin, new Uri(m.Groups[1].Value).AbsolutePath).ToString()).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return Array.Empty<string>();
        }
    }

    private static async Task<IEnumerable<CrawlIssue>> ExternalAsync(IReadOnlyList<string> urls, CrawlSettings settings)
    {
        var issues = new List<CrawlIssue>();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 " + settings.UserAgent);
        foreach (var url in urls)
        {
            try
            {
                using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                var status = (int)response.StatusCode;
                if (status is >= 400 and not (401 or 403 or 429 or 999))   // those mean "we won't talk to a robot", not "broken"
                {
                    issues.Add(new CrawlIssue(Severity.Warning, "(external link)", $"{url} answered {status}."));
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                issues.Add(new CrawlIssue(Severity.Warning, "(external link)", $"{url} could not be reached ({ex.GetType().Name})."));
            }
        }

        return issues;
    }

    private static string Clip(string text) => text.Length <= 160 ? text.Replace('\n', ' ') : text[..160].Replace('\n', ' ') + "...";
}
