/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.Json;
using System.Text.RegularExpressions;
using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>One test that mentions a page: which project and file, which test method, and what kind of test it is (unit or web, component, browser).</summary>
internal sealed record TestRef(string Project, string File, string Method, string Layer);

/// <summary>One page of the site as the feature map lists it.</summary>
internal sealed record PageRow(string Route, string File, string Area, int? Clicks, string? Via, bool Parameters, bool SignedIn, bool Crawled, IReadOnlyList<TestRef> Tests);

/// <summary>
/// The first layer of the feature map, worked out from the code alone: every routable page, how many clicks it takes to reach from the home page by following the links in the markup (the page's own, its layout's and
/// those of the components it shows), which tests mention it, and whether the site crawler would visit it. Pages nothing links to are reported as having no way in - reached by a redirect, a code or a QR, or not at all.
/// </summary>
internal static partial class FeatureMap
{
    [GeneratedRegex("""^@page\s+"([^"]+)"\s*$""", RegexOptions.Multiline)]
    private static partial Regex PageDirective();

    // A link to this site is "/play", "play" or ""; one with a scheme ("https:", "mailto:") or a fragment leaves it and is not a click inside the site.
    [GeneratedRegex("""(?:href\s*=\s*"@\(\$?|href\s*=\s*\$?@?|NavigateTo\(\s*\$?@?)"(?![a-zA-Z][a-zA-Z0-9+.-]*:|#)([^"?#]*)""")]
    private static partial Regex LinkTarget();

    [GeneratedRegex("""<([A-Z][A-Za-z0-9]+)[\s/>]""")]
    private static partial Regex ComponentTag();

    [GeneratedRegex("""^@layout\s+(?:[\w.]*\.)?(\w+)\s*$""", RegexOptions.Multiline)]
    private static partial Regex LayoutDirective();

    [GeneratedRegex("""public\s+(?:async\s+)?(?:Task|void)\s+(\w+)\s*\(""")]
    private static partial Regex TestMethod();

    public static IReadOnlyList<PageRow> Build(string root, ToolConfig config)
    {
        var razor = ReadRazor(root, config);
        var pages = DiscoverPages(razor);
        var clicks = Depths(pages, razor);
        var tests = ReadTests(root, config);
        var skip = string.IsNullOrWhiteSpace(config.Crawl.SkipPattern) ? null : new Regex(config.Crawl.SkipPattern, RegexOptions.IgnoreCase);
        return pages.Select(p => RowFor(p, clicks, tests, skip, config)).OrderBy(r => r.Area, StringComparer.Ordinal).ThenBy(r => r.Route, StringComparer.Ordinal).ToList();
    }

    private static PageRow RowFor(PageInfo page, Dictionary<string, (int Clicks, string Via)> clicks, IReadOnlyList<(string Project, string File, string Text)> tests, Regex? skip, ToolConfig config)
    {
        clicks.TryGetValue(page.Route, out var reached);
        var signedIn = config.Features?.SignedInPrefixes.Any(p => page.Route.StartsWith(p, StringComparison.OrdinalIgnoreCase)) == true;
        var crawled = reached != default && !page.Parameters && !signedIn && skip?.IsMatch(page.Route) != true;
        return new PageRow(page.Route, page.File, page.Area, reached == default ? null : reached.Clicks, reached == default ? null : reached.Via, page.Parameters, signedIn, crawled, TestsFor(page, tests));
    }

    // ---- pages and their links ----

    internal sealed record PageInfo(string Route, string File, string Area, bool Parameters, string Stem);

    internal static List<PageInfo> DiscoverPages(IReadOnlyDictionary<string, string> razor)
    {
        var pages = new List<PageInfo>();
        foreach (var (file, text) in razor)
        {
            foreach (Match match in PageDirective().Matches(text))
            {
                var route = match.Groups[1].Value;
                pages.Add(new PageInfo(route, file, AreaOf(route), route.Contains('{', StringComparison.Ordinal), Path.GetFileNameWithoutExtension(file)));
            }
        }

        return pages;
    }

    internal static string AreaOf(string route)
    {
        var first = route.Trim('/').Split('/')[0];
        return first.Length == 0 ? "home" : first.Contains('{', StringComparison.Ordinal) ? "home" : first;
    }

    /// <summary>The routes a file links to, directly or through the components it shows and its layout. Route parameters become <c>{}</c>.</summary>
    internal static HashSet<string> LinksOf(string file, IReadOnlyDictionary<string, string> razor, IReadOnlyDictionary<string, string> byStem, HashSet<string>? seen = null)
    {
        seen ??= [];
        var links = new HashSet<string>(StringComparer.Ordinal);
        if (!seen.Add(file) || !razor.TryGetValue(file, out var text))
        {
            return links;
        }

        foreach (Match match in LinkTarget().Matches(text))
        {
            links.Add(Normalize(match.Groups[1].Value));
        }

        foreach (var stem in ComponentTag().Matches(text).Select(m => m.Groups[1].Value).Distinct().Where(byStem.ContainsKey))
        {
            links.UnionWith(LinksOf(byStem[stem], razor, byStem, seen));
        }

        return links;
    }

    internal static string Normalize(string path) =>
        "/" + string.Join('/', path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Select(s => s.StartsWith('@') || s.Contains('{', StringComparison.Ordinal) ? "{}" : s));

    /// <summary>Does a link (with <c>{}</c> where a value goes) lead to this page's route template?</summary>
    internal static bool Leads(string link, string route)
    {
        var a = link.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var b = route.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return a.Length == b.Length && a.Zip(b).All(p => p.First == "{}" || p.Second.StartsWith('{') || string.Equals(p.First, p.Second, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Clicks from the home page to each page, by breadth-first search over the links, with the page the shortest path comes through.</summary>
    internal static Dictionary<string, (int Clicks, string Via)> Depths(IReadOnlyList<PageInfo> pages, IReadOnlyDictionary<string, string> razor)
    {
        var byStem = razor.Keys.GroupBy(f => Path.GetFileNameWithoutExtension(f)).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var linksByPage = pages.GroupBy(p => p.Route).ToDictionary(g => g.Key, g => PageLinks(g.First(), razor, byStem));
        var depth = new Dictionary<string, (int, string)>(StringComparer.Ordinal);
        if (!linksByPage.ContainsKey("/"))
        {
            return depth;
        }

        depth["/"] = (0, "-");
        var queue = new Queue<string>(["/"]);
        while (queue.Count > 0)
        {
            var from = queue.Dequeue();
            foreach (var target in pages.Select(p => p.Route).Distinct().Where(r => !depth.ContainsKey(r) && linksByPage[from].Any(l => Leads(l, r))))
            {
                depth[target] = (depth[from].Item1 + 1, from);
                queue.Enqueue(target);
            }
        }

        return depth.ToDictionary(d => d.Key, d => (d.Value.Item1, d.Value.Item2), StringComparer.Ordinal);
    }

    private static HashSet<string> PageLinks(PageInfo page, IReadOnlyDictionary<string, string> razor, IReadOnlyDictionary<string, string> byStem)
    {
        var links = LinksOf(page.File, razor, byStem);
        var layout = LayoutDirective().Match(razor[page.File]) is { Success: true } m ? m.Groups[1].Value : "MainLayout";
        if (byStem.TryGetValue(layout, out var layoutFile))
        {
            links.UnionWith(LinksOf(layoutFile, razor, byStem));
        }

        return links;
    }

    // ---- tests that mention a page ----

    private static List<TestRef> TestsFor(PageInfo page, IReadOnlyList<(string Project, string File, string Text)> tests)
    {
        var literal = page.Route.Contains('{', StringComparison.Ordinal) ? "\"" + page.Route[..page.Route.IndexOf('{', StringComparison.Ordinal)] : "\"" + page.Route + "\"";
        var component = new Regex(@"(?:<|typeof\()" + Regex.Escape(page.Stem) + @"[>)]|\bRender<" + Regex.Escape(page.Stem) + ">");
        var found = new List<TestRef>();
        foreach (var (project, file, text) in tests)
        {
            var offsets = IndexesOf(text, literal).Concat(component.Matches(text).Select(m => m.Index)).Distinct();
            found.AddRange(offsets.Select(o => new TestRef(project, file, MethodAt(text, o), LayerOf(project, file))).Distinct());
        }

        return found.Distinct().ToList();
    }

    private static IEnumerable<int> IndexesOf(string text, string needle)
    {
        for (var i = text.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            yield return i;
        }
    }

    private static string MethodAt(string text, int offset)
    {
        var last = TestMethod().Matches(text[..offset]).LastOrDefault();
        return last?.Groups[1].Value ?? "(class)";
    }

    internal static string LayerOf(string project, string file) =>
        project.Contains("Browser", StringComparison.OrdinalIgnoreCase) ? "browser" : file.Contains("/Components/", StringComparison.Ordinal) ? "component" : "unit/web";

    // ---- reading the source ----

    private static Dictionary<string, string> ReadRazor(string root, ToolConfig config) =>
        Files(root, config.SourceFolders, "*.razor", config).ToDictionary(f => f.Relative, f => File.ReadAllText(f.Path), StringComparer.Ordinal);

    private static List<(string Project, string File, string Text)> ReadTests(string root, ToolConfig config) =>
        Files(root, config.TestFolders, "*.cs", config).Select(f => (ProjectOf(f.Relative), f.Relative, File.ReadAllText(f.Path))).ToList();

    private static string ProjectOf(string relative)
    {
        var parts = relative.Split('/');
        return parts.Length > 1 ? parts[1] : parts[0];
    }

    private static IEnumerable<(string Path, string Relative)> Files(string root, string[] folders, string pattern, ToolConfig config)
    {
        foreach (var folder in folders.Select(f => Path.Combine(root, f)).Where(Directory.Exists))
        {
            foreach (var path in Directory.EnumerateFiles(folder, pattern, SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (!relative.Contains("/obj/", StringComparison.Ordinal) && !relative.Contains("/bin/", StringComparison.Ordinal) && !config.SkipFolders.Any(s => ("/" + relative).Contains(s, StringComparison.Ordinal)))
                {
                    yield return (path, relative);
                }
            }
        }
    }

    // ---- the page ----

    private static readonly JsonSerializerOptions CamelCase = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Render(string title, IReadOnlyList<PageRow> rows)
    {
        var json = JsonSerializer.Serialize(new { generated = DateTime.Now.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture), pages = rows }, CamelCase).Replace("<", "\\u003c", StringComparison.Ordinal);
        return FeatureMapPage.Template.Replace("/*DATA*/", json, StringComparison.Ordinal).Replace("/*TITLE*/", System.Net.WebUtility.HtmlEncode(title), StringComparison.Ordinal);
    }

    public static string Write(string root, ToolConfig config)
    {
        var path = Path.Combine(Repo.OutputRoot(root), "reports", "feature-map.html");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Render(config.Title(root), Build(root, config)));
        return path;
    }
}
