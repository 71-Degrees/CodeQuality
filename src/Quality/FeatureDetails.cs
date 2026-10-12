/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.RegularExpressions;
using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>One thing a function's pages read or call, and how the site's performance notes classify it (Cached, Uncached, HttpCached, ...), when they do.</summary>
internal sealed record Dependency(string Name, string? CacheClass);

/// <summary>
/// What is behind a function, worked out from the code: the services and stores its pages ask for and how each is cached, how often its source changed lately (a function that keeps changing and is lightly tested is the
/// risky one), which docs mention its pages, and whether any of its source sits on a path that decides who can do what or takes payment.
/// </summary>
internal sealed record FunctionDetails(IReadOnlyList<string> Files, IReadOnlyList<Dependency> Dependencies, int Changes, IReadOnlyList<string> Docs, IReadOnlyList<string> Sensitive, IReadOnlyList<string> UnfoundSteps);

internal static partial class FeatureDetails
{
    [GeneratedRegex(@"^@inject\s+([\w.<>]+)\s+\w+", RegexOptions.Multiline)]
    private static partial Regex Inject();

    [GeneratedRegex(@"^\|\s*`([^`]+)`\s*\|\s*([A-Za-z]+)\s*\|", RegexOptions.Multiline)]
    private static partial Regex ClassifiedRow();

    [GeneratedRegex(@"<([A-Z][A-Za-z0-9]+)[\s/>]")]
    private static partial Regex ComponentTag();

    private static readonly string[] Interesting = ["Repository", "Store", "Client", "Service", "Gateway", "Processor", "Access", "Conductor", "Gate", "Mailer", "Recorder"];

    /// <summary>A page's own file and the files of the components it shows, directly or through each other (layouts are left out: they belong to every page).</summary>
    internal static List<string> FilesOf(string file, IReadOnlyDictionary<string, string> razor, IReadOnlyDictionary<string, string> byStem, HashSet<string>? seen = null)
    {
        seen ??= [];
        var files = new List<string>();
        if (!seen.Add(file) || !razor.TryGetValue(file, out var text))
        {
            return files;
        }

        files.Add(file);
        foreach (var stem in ComponentTag().Matches(text).Select(m => m.Groups[1].Value).Distinct().Where(byStem.ContainsKey))
        {
            files.AddRange(FilesOf(byStem[stem], razor, byStem, seen));
        }

        return files;
    }

    /// <summary>The services and stores the files ask for (<c>@inject</c>), framework plumbing left out, each with its classification from the performance notes.</summary>
    internal static List<Dependency> DependenciesOf(IEnumerable<string> files, IReadOnlyDictionary<string, string> razor, IReadOnlyDictionary<string, string> classes)
    {
        var names = files.Where(razor.ContainsKey).SelectMany(f => Inject().Matches(razor[f]).Select(m => ShortName(m.Groups[1].Value))).Distinct(StringComparer.Ordinal);
        return names
            .Where(n => classes.ContainsKey(n) || Interesting.Any(i => n.EndsWith(i, StringComparison.Ordinal)))
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => new Dependency(n, classes.GetValueOrDefault(n)))
            .ToList();
    }

    private static string ShortName(string typeName) => typeName[(typeName.LastIndexOf('.') + 1)..];

    /// <summary>Each row of the performance notes that names a type and gives its class: <c>| `IQuizRepository` | Cached | ... |</c>.</summary>
    internal static Dictionary<string, string> ParseClasses(string markdown)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match row in ClassifiedRow().Matches(markdown))
        {
            found.TryAdd(row.Groups[1].Value, row.Groups[2].Value);
        }

        return found;
    }

    public static Dictionary<string, string> ReadClasses(string root, FeatureSettings? settings)
    {
        var path = Path.Combine(root, settings?.PerformanceDoc ?? "docs/performance.md");
        return File.Exists(path) ? ParseClasses(File.ReadAllText(path)) : new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>The commits of the last <paramref name="days"/> days that touched each file, from one git call (<c>--name-only</c>), so a function can count its distinct commits.</summary>
    public static async Task<Dictionary<string, HashSet<string>>> ReadChangesAsync(string root, int days)
    {
        var log = await Shell.RunAsync("git", $"log --since={days}.days --name-only --format=@@%h", root, 120);
        return ParseChanges(log.Code == 0 ? log.Output : string.Empty);
    }

    internal static Dictionary<string, HashSet<string>> ParseChanges(string log)
    {
        var byFile = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var commit = string.Empty;
        foreach (var line in log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                commit = line[2..];
            }
            else if (!byFile.TryAdd(line, [commit]))
            {
                byFile[line].Add(commit);
            }
        }

        return byFile;
    }

    /// <summary>
    /// The docs (file names) that mention one of the function's pages as code, such as `/host/` or `/quizzes/edit`. A route written in backticks and not followed by more path or word characters, so "/team" in a
    /// sentence, or "/quizzes" inside "/quizzes/edit", is not a mention.
    /// </summary>
    internal static List<string> DocsMentioning(IEnumerable<string> routes, IReadOnlyDictionary<string, string> docs)
    {
        var patterns = routes.Select(MentionOf).OfType<Regex>().ToList();
        return docs.Where(d => patterns.Any(p => p.IsMatch(d.Value))).Select(d => d.Key).OrderBy(d => d, StringComparer.Ordinal).ToList();
    }

    // A route with a value in it ("/host/{id}") is matched by its fixed start; one without must end there. The home page ("/") is in every doc and says nothing.
    private static Regex? MentionOf(string route)
    {
        var parameterised = route.Contains('{', StringComparison.Ordinal);
        var fixedPart = parameterised ? route[..route.IndexOf('{', StringComparison.Ordinal)] : route;
        return fixedPart.Length <= 1 ? null : new Regex("`" + Regex.Escape(fixedPart) + (parameterised ? string.Empty : @"(?![\w/-])"));
    }

    public static Dictionary<string, string> ReadDocs(string root, FeatureSettings? settings)
    {
        var folder = Path.Combine(root, settings?.DocsFolder ?? "docs");
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.md").ToDictionary(f => Path.GetFileName(f), File.ReadAllText, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly string[] Generic = ["a ", "an ", "the ", "any ", "my ", "one ", "each ", "all ", "another "];

    /// <summary>What a "Click ..." step names: the text of a button or link (alternatives separated by " or "). A step that describes a click in general ("Click an answer", "Click the flag beside it") names no control and is left alone.</summary>
    internal static IEnumerable<string> ControlsNamedBy(string step)
    {
        const string Prefix = "Click ";
        if (!step.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        var rest = step[Prefix.Length..].Trim();
        if (Generic.Any(g => rest.StartsWith(g, StringComparison.OrdinalIgnoreCase)))
        {
            yield break;
        }

        foreach (var alternative in rest.Split(" or ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return alternative;
        }
    }

    /// <summary>The "Click" steps that name a control whose text appears nowhere in the site's pages: a step the list claims and no person can take.</summary>
    internal static List<string> UnfoundSteps(IEnumerable<string> steps, string corpus) =>
        steps.Where(step => ControlsNamedBy(step).Any(name => !corpus.Contains(name, StringComparison.OrdinalIgnoreCase))).ToList();

    public static FunctionDetails For(
        FeatureFunction function, IReadOnlyList<FeatureMap.PageInfo> pages, IReadOnlyDictionary<string, string> razor, IReadOnlyDictionary<string, string> byStem,
        IReadOnlyDictionary<string, string> classes, IReadOnlyDictionary<string, HashSet<string>> changes, IReadOnlyDictionary<string, string> docs, IEnumerable<string> sensitive, string? corpus = null)
    {
        var files = function.Routes
            .SelectMany(r => pages.Where(p => string.Equals(p.Route, r, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(p => FilesOf(p.File, razor, byStem))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var commits = files.SelectMany(f => changes.TryGetValue(f, out var c) ? c : []).Distinct(StringComparer.Ordinal).Count();
        var explaining = DocsMentioning(function.Routes, docs).Union(function.Docs.Where(docs.ContainsKey)).OrderBy(d => d, StringComparer.Ordinal).ToList();
        return new FunctionDetails(files, DependenciesOf(files, razor, classes), commits, explaining, ReleasePanel.Sensitive(files, sensitive), UnfoundSteps(function.Steps, corpus ?? string.Join(Environment.NewLine, razor.Values)));
    }
}
