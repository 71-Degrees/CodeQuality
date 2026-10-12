/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.RegularExpressions;
using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>A test class (or one test) a function names, with how many tests it holds and what kind they are.</summary>
internal sealed record TestCover(string Name, string Project, string Layer, int Count);

/// <summary>One function of the site, checked against the code: how many clicks, which tests cover it, whether they exist, whether the crawler could reach it.</summary>
internal sealed record FunctionRow(
    string Id, string Name, string Area, string Who, IReadOnlyList<string> Steps, int Clicks, int Entries, IReadOnlyList<string> Routes, bool Walked,
    IReadOnlyList<TestCover> Tests, IReadOnlyList<string> Missing, int TestCount, string Status, string Crawl, int? NavigationDepth, string? Notes);

/// <summary>What the check found: the functions, the pages that belong to none, and the routes a function names that are not pages.</summary>
internal sealed record UnmappedTests(string Class, string Project, int Count);

internal sealed record FunctionsResult(IReadOnlyList<FunctionRow> Functions, IReadOnlyList<string> UnassignedPages, IReadOnlyList<(string Function, string Route)> UnknownRoutes, IReadOnlyList<UnmappedTests> Unmapped);

internal sealed record TestClassInfo(string Name, string Project, string File, string Layer, IReadOnlyList<string> Tests);

/// <summary>
/// Checks the curated list of functions against the code: every route a function names must be a page, every page must belong to a function, and every test named must exist. A function is then "not covered"
/// (no test found), covered by unit, web or component tests only, or covered by a browser test.
/// </summary>
internal static partial class FeatureFunctions
{
    // Top-level classes only (at the start of a line): a test class often declares fakes above its tests, and those must not take the tests.
    [GeneratedRegex(@"^(?:public|internal)\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+)*class\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex ClassDeclaration();

    // A test is a Fact, Theory or BrowserFact line, any further attribute lines (a Theory's data can hold a "]" inside a string, so attributes are read by line, not by bracket), then the method.
    [GeneratedRegex(@"^[ \t]*\[(?:Fact|Theory|BrowserFact)\b[^\n]*\n(?:[ \t]*\[[^\n]*\n)*[ \t]*public\s+(?:async\s+)?(?:Task|void)\s+(\w+)", RegexOptions.Multiline)]
    private static partial Regex TestDeclaration();

    /// <summary>Every test class in the test sources and the tests in it. A test belongs to the nearest class declared above it in its file.</summary>
    public static List<TestClassInfo> IndexTests(IEnumerable<(string Project, string File, string Text)> sources)
    {
        var found = new List<TestClassInfo>();
        foreach (var (project, file, text) in sources)
        {
            var classes = ClassDeclaration().Matches(text).Select(m => (m.Index, Name: m.Groups[1].Value)).ToList();
            var tests = TestDeclaration().Matches(text).Select(m => (m.Index, Method: m.Groups[1].Value)).ToList();
            foreach (var (index, name) in classes)
            {
                var next = classes.Where(c => c.Index > index).Select(c => c.Index).DefaultIfEmpty(int.MaxValue).Min();
                var mine = tests.Where(t => t.Index > index && t.Index < next).Select(t => t.Method).ToList();
                found.Add(new TestClassInfo(name, project, file, FeatureMap.LayerOf(project, file), mine));
            }
        }

        return found;
    }

    public static FunctionsResult Resolve(FeatureRegistryFile registry, IReadOnlyList<PageRow> pages, IReadOnlyList<TestClassInfo> tests)
    {
        var rows = registry.Functions.Select(f => RowFor(f, pages, tests)).ToList();
        var routed = registry.Functions.SelectMany(f => f.Routes).Concat(registry.NotFunctions).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unassigned = pages.Select(p => p.Route).Where(r => !routed.Contains(r)).Distinct().OrderBy(r => r, StringComparer.Ordinal).ToList();
        var known = pages.Select(p => p.Route).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = registry.Functions.SelectMany(f => f.Routes.Where(r => !known.Contains(r)).Select(r => (f.Id, r))).ToList();
        var named = registry.Functions.SelectMany(f => f.Tests.Select(t => t.Split('.')[0])).ToHashSet(StringComparer.Ordinal);
        var unmapped = tests.Where(t => t.Tests.Count > 0 && !named.Contains(t.Name)).Select(t => new UnmappedTests(t.Name, t.Project, t.Tests.Count)).OrderByDescending(t => t.Count).ToList();
        return new FunctionsResult(rows, unassigned, unknown, unmapped);
    }

    private static FunctionRow RowFor(FeatureFunction f, IReadOnlyList<PageRow> pages, IReadOnlyList<TestClassInfo> tests)
    {
        var covers = new List<TestCover>();
        var missing = new List<string>();
        foreach (var reference in f.Tests)
        {
            var matches = Find(reference, tests);
            if (matches.Count == 0)
            {
                missing.Add(reference);
            }

            covers.AddRange(matches);
        }

        var mine = f.Routes.Select(r => pages.FirstOrDefault(p => string.Equals(p.Route, r, StringComparison.OrdinalIgnoreCase))).Where(p => p is not null).Select(p => p!).ToList();
        var clicks = f.Steps.Count(s => s.StartsWith("Click", StringComparison.OrdinalIgnoreCase));
        var depths = mine.Where(p => p.Clicks is not null).Select(p => p.Clicks!.Value).ToList();
        return new FunctionRow(f.Id, f.Name, f.Area, f.Who, f.Steps, clicks, f.Steps.Length - clicks, f.Routes, f.Walked, covers, missing, covers.Sum(c => c.Count), StatusOf(covers), CrawlOf(mine), depths.Count > 0 ? depths.Min() : null, f.Notes);
    }

    private static List<TestCover> Find(string reference, IReadOnlyList<TestClassInfo> tests)
    {
        var dot = reference.IndexOf('.', StringComparison.Ordinal);
        var className = dot < 0 ? reference : reference[..dot];
        var method = dot < 0 ? null : reference[(dot + 1)..];
        return tests
            .Where(t => t.Name == className && (method is null || t.Tests.Contains(method)))
            .Select(t => new TestCover(reference, t.Project, t.Layer, method is null ? t.Tests.Count : 1))
            .Where(c => c.Count > 0)
            .ToList();
    }

    internal static string StatusOf(IReadOnlyList<TestCover> covers) =>
        covers.Count == 0 ? "none" : covers.Any(c => c.Layer == "browser") ? "browser" : "weak";

    internal static string CrawlOf(IReadOnlyList<PageRow> pages)
    {
        if (pages.Count == 0)
        {
            return "not a page";
        }

        if (pages.All(p => p.Crawled))
        {
            return "crawled";
        }

        return pages.Any(p => p.SignedIn) ? "needs sign-in" : pages.Any(p => p.Parameters) ? "needs an id" : "not crawled";
    }
}
