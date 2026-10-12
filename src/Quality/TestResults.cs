/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Globalization;
using System.Xml.Linq;
using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>One test project's last run.</summary>
/// <param name="At">When the project last ran (the time its results were written).</param>
/// <param name="IsBrowser">A real-browser test project: kept apart from the unit and component tests in the report, and out of the coverage (the site runs in its own process).</param>
internal sealed record ProjectTests(string Name, int Total, int Passed, int Failed, int Skipped, double Seconds, List<string> Failures, string At = "", bool IsBrowser = false);

/// <summary>Line coverage of one part of the source (a project folder): lines the tests ran, of the lines the tests loaded.</summary>
internal sealed record CoverageGroup(string Name, int Covered, int Lines)
{
    public double Rate => Lines == 0 ? 0 : (double)Covered / Lines;
}

/// <summary>What the test projects last did: how many tests, how many passed, and how much of the source they ran.</summary>
internal sealed class TestSummary
{
    /// <summary>When the results were written.</summary>
    public string At { get; set; } = string.Empty;

    public List<ProjectTests> Projects { get; set; } = new();

    public List<CoverageGroup> Coverage { get; set; } = new();

    /// <summary>Unit and component tests together (the browser tests are counted apart, <see cref="Browser"/>).</summary>
    public int Total => Projects.Where(p => !p.IsBrowser).Sum(p => p.Total);

    public int Passed => Projects.Where(p => !p.IsBrowser).Sum(p => p.Passed);

    public int Failed => Projects.Where(p => !p.IsBrowser).Sum(p => p.Failed);

    public int Skipped => Projects.Where(p => !p.IsBrowser).Sum(p => p.Skipped);

    /// <summary>The real-browser test projects.</summary>
    public IReadOnlyList<ProjectTests> Browser => Projects.Where(p => p.IsBrowser).ToList();

    public int CoveredLines => Coverage.Sum(g => g.Covered);

    public int CoverableLines => Coverage.Sum(g => g.Lines);

    /// <summary>All the measured source together, or null when no coverage was collected.</summary>
    public double? CoverageRate => CoverableLines == 0 ? null : (double)CoveredLines / CoverableLines;
}

/// <summary>
/// Reads what the test projects wrote: a TRX file (counts, failures, time) and a Cobertura file (which lines ran) per project, under <c>reports/test-results/&lt;project&gt;</c>. The pre-push check writes them
/// every time it runs the tests, and <c>quality tests</c> writes them on demand, so a report is never more than a push old. Only C# source counts for coverage: Razor components are compiled into generated code the
/// coverage cannot map back to the page, so they are not in the numbers.
/// </summary>
internal static class TestResults
{
    private static readonly XNamespace Trx = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static string ResultsFolder(string root) => Path.Combine(Repo.OutputRoot(root), "reports", "test-results");

    /// <summary>The summary of whatever is in the results folder, or null when no test project has written results yet.</summary>
    public static TestSummary? Read(string root, ToolConfig config)
    {
        var folder = ResultsFolder(root);
        if (!Directory.Exists(folder))
        {
            return null;
        }

        var summary = new TestSummary();
        var lines = new Dictionary<string, Dictionary<int, int>>(StringComparer.OrdinalIgnoreCase);
        DateTime? newest = null;
        foreach (var project in Directory.EnumerateDirectories(folder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var trx = Path.Combine(project, "results.trx");
            if (!File.Exists(trx))
            {
                continue;
            }

            var name = Path.GetFileName(project);
            var isBrowser = IsBrowserProject(name);
            var wrote = File.GetLastWriteTime(trx);
            summary.Projects.Add(ParseTrx(name, File.ReadAllText(trx)) with { At = wrote.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), IsBrowser = isBrowser });
            if (isBrowser)
            {
                continue;
            }

            newest = Max(newest, wrote);
            foreach (var cobertura in Directory.EnumerateFiles(project, "coverage.cobertura.xml", SearchOption.AllDirectories))
            {
                MergeCoverage(lines, File.ReadAllText(cobertura));
            }
        }

        if (summary.Projects.Count == 0)
        {
            return null;
        }

        summary.At = (newest ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        summary.Coverage = Summarize(lines, root, config);
        return summary;
    }

    /// <summary>A test project that drives a real browser (it has "Browser" in its name): the site runs in its own process, so its coverage says nothing about the code, and it runs on demand rather than on every push.</summary>
    internal static bool IsBrowserProject(string name) => name.Contains("Browser", StringComparison.OrdinalIgnoreCase);

    private static DateTime Max(DateTime? a, DateTime b) => a is null || b > a ? b : a.Value;

    /// <summary>The counts, failures and time out of a TRX file.</summary>
    internal static ProjectTests ParseTrx(string name, string xml)
    {
        var doc = XDocument.Parse(xml);
        var counters = doc.Descendants(Trx + "Counters").FirstOrDefault();
        var total = IntOf(counters, "total");
        var passed = IntOf(counters, "passed");
        var failed = IntOf(counters, "failed");

        var times = doc.Descendants(Trx + "Times").FirstOrDefault();
        var seconds = times is not null && DateTime.TryParse((string?)times.Attribute("start"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start)
            && DateTime.TryParse((string?)times.Attribute("finish"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var finish)
            ? Math.Round((finish - start).TotalSeconds, 1)
            : 0;

        var failures = doc.Descendants(Trx + "UnitTestResult")
            .Where(r => string.Equals((string?)r.Attribute("outcome"), "Failed", StringComparison.OrdinalIgnoreCase))
            .Select(r => (string?)r.Attribute("testName") ?? "(unnamed test)")
            .Take(25)
            .ToList();

        return new ProjectTests(name, total, passed, failed, Math.Max(0, total - passed - failed), seconds, failures);
    }

    private static int IntOf(XElement? element, string attribute) =>
        int.TryParse((string?)element?.Attribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    /// <summary>Adds one Cobertura report to the running picture: for each source file, the most times each line ran in any project's tests (a line ran if any project ran it).</summary>
    internal static void MergeCoverage(Dictionary<string, Dictionary<int, int>> files, string xml)
    {
        foreach (var @class in XDocument.Parse(xml).Descendants("class"))
        {
            var file = ((string?)@class.Attribute("filename") ?? string.Empty).Replace('\\', '/');
            if (file.Length == 0)
            {
                continue;
            }

            if (!files.TryGetValue(file, out var known))
            {
                files[file] = known = new Dictionary<int, int>();
            }

            foreach (var line in @class.Descendants("line"))
            {
                if (int.TryParse((string?)line.Attribute("number"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                    && int.TryParse((string?)line.Attribute("hits"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hits))
                {
                    known[number] = Math.Max(known.GetValueOrDefault(number), hits);
                }
            }
        }
    }

    /// <summary>Line coverage per project folder, over our own C# source only (not generated code, the tests themselves, or the shared submodules the config skips).</summary>
    internal static List<CoverageGroup> Summarize(Dictionary<string, Dictionary<int, int>> files, string root, ToolConfig config)
    {
        var rootPath = root.Replace('\\', '/').TrimEnd('/') + "/";
        var groups = new Dictionary<string, (int Covered, int Lines)>(StringComparer.Ordinal);
        foreach (var (reported, lines) in files)
        {
            // A deterministic build reports paths relative to the repository root; others report absolute ones.
            var file = Path.IsPathRooted(reported) || reported.StartsWith('/') ? reported : rootPath + reported;
            if (!IsOurSource(file, rootPath, config))
            {
                continue;
            }

            var relative = file[rootPath.Length..];
            var parts = relative.Split('/');
            var name = parts.Length >= 2 ? parts[0] + "/" + parts[1] : relative;
            var (covered, total) = groups.GetValueOrDefault(name);
            groups[name] = (covered + lines.Values.Count(h => h > 0), total + lines.Count);
        }

        return groups.OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new CoverageGroup(g.Key, g.Value.Covered, g.Value.Lines)).ToList();
    }

    private static bool IsOurSource(string file, string rootPath, ToolConfig config)
    {
        if (!file.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase)
            || file.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = "/" + file[rootPath.Length..];
        return config.SourceFolders.Any(f => relative.StartsWith("/" + f.Trim('/') + "/", StringComparison.OrdinalIgnoreCase))
            && !config.TestFolders.Any(f => relative.StartsWith("/" + f.Trim('/') + "/", StringComparison.OrdinalIgnoreCase))
            && !config.SkipFolders.Any(s => relative.Contains(s, StringComparison.OrdinalIgnoreCase));
    }
}
