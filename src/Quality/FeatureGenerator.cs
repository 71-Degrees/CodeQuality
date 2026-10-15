/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>What the generator drafted: the functions, the pages set aside as "not a function", and how many pages were already covered by a hand-written entry.</summary>
internal sealed record GeneratedFeatures(IReadOnlyList<FeatureFunction> Functions, IReadOnlyList<string> NotFunctions, int AlreadyCovered);

/// <summary>
/// <c>quality features --generate</c>: drafts <c>quality/features.json</c> from the code alone - no tokens. A function per page group (the pages under one route prefix; a big prefix splits to a page each) named from the
/// page title, who may use it from the <c>[Authorize]</c> attribute, its steps from the real link text on the click path from the home page and the labels and buttons on the page, its tests from the test classes that
/// mention the page. Entries are marked <c>generated</c> so the dashboard can say how much of the list nobody has reviewed. A list that already exists is never rewritten (it has comments and hand-written wording):
/// the drafts for pages it does not cover go to <c>reports/features-proposed.json</c> to copy from.
/// </summary>
internal static partial class FeatureGenerator
{
    private const int LargestGroup = 4;
    private const int MostFormSteps = 4;
    private const int MostTests = 8;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };

    [GeneratedRegex("""<PageTitle>(.*?)</PageTitle>""", RegexOptions.Singleline)]
    private static partial Regex Title();

    [GeneratedRegex("""<h1[^>]*>(.*?)</h1>""", RegexOptions.Singleline)]
    private static partial Regex Heading();

    [GeneratedRegex("""<label\b[^>]*>(.*?)</label>""", RegexOptions.Singleline)]
    private static partial Regex Label();

    [GeneratedRegex("""<button\b[^>]*>(.*?)</button>""", RegexOptions.Singleline)]
    private static partial Regex Button();

    [GeneratedRegex("""@attribute\s+\[Authorize(?:\((?<args>[^)]*)\))?\]""")]
    private static partial Regex Authorize();

    [GeneratedRegex("""<[^>]+>|@\(?[\w.\[\]]+\)?""")]
    private static partial Regex Markup();

    [GeneratedRegex("""^/(error|not-?found|404)\b""", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorRoute();

    internal static async Task<GeneratedFeatures> DraftAsync(string root, ToolConfig config)
    {
        var (rows, _) = await FeatureMap.AnalyzeAsync(root, config);
        var razor = FeatureMap.ReadRazor(root, config);
        var classes = FeatureFunctions.IndexTests(FeatureMap.ReadTests(root, config));
        var existing = FeatureRegistryFile.Load(root, config.Features);
        var covered = new HashSet<string>(existing?.Functions.SelectMany(f => f.Routes).Concat(existing.NotFunctions) ?? [], StringComparer.OrdinalIgnoreCase);
        var notFunctions = rows.Select(r => r.Route).Where(r => ErrorRoute().IsMatch(r) && !covered.Contains(r)).Distinct().ToList();
        var todo = rows.Where(r => !covered.Contains(r.Route) && !notFunctions.Contains(r.Route)).ToList();
        var byRoute = rows.GroupBy(r => r.Route).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var context = new DraftContext(byRoute, razor, classes);
        var ids = new HashSet<string>(existing?.Functions.Select(f => f.Id) ?? [], StringComparer.Ordinal);
        var functions = Groups(todo).Select(group => Draft(group, context, ids)).ToList();
        return new GeneratedFeatures(functions, notFunctions, rows.Count - todo.Count - notFunctions.Count);
    }

    /// <summary>Writes quality/features.json when there is none; otherwise the drafts for uncovered pages go to reports/features-proposed.json. Returns the file written.</summary>
    internal static async Task<(string Path, bool Proposed, GeneratedFeatures Drafted)> WriteAsync(string root, ToolConfig config)
    {
        var drafted = await DraftAsync(root, config);
        var registryPath = Path.Combine(root, config.Features?.Registry ?? "quality/features.json");
        var proposed = File.Exists(registryPath);
        var path = proposed ? Path.Combine(Repo.OutputRoot(root), "reports", "features-proposed.json") : registryPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var body = JsonSerializer.Serialize(new FeatureRegistryFile { NotFunctions = [.. drafted.NotFunctions], Functions = [.. drafted.Functions] }, Json);
        await File.WriteAllTextAsync(path, body + Environment.NewLine);
        return (path, proposed, drafted);
    }

    private sealed record DraftContext(IReadOnlyDictionary<string, PageRow> Rows, IReadOnlyDictionary<string, string> Razor, IReadOnlyList<TestClassInfo> Classes);

    /// <summary>Pages under one route prefix are one function while they number no more than the largest group; a bigger prefix gets a function per page.</summary>
    private static IEnumerable<List<PageRow>> Groups(IEnumerable<PageRow> pages)
    {
        foreach (var group in pages.GroupBy(p => p.Route.Trim('/').Split('/')[0].ToLowerInvariant()))
        {
            var members = group.OrderBy(p => p.Clicks ?? int.MaxValue).ThenBy(p => p.Route.Length).ToList();
            if (members.Count <= LargestGroup)
            {
                yield return members;
                continue;
            }

            foreach (var page in members)
            {
                yield return [page];
            }
        }
    }

    private static FeatureFunction Draft(List<PageRow> group, DraftContext context, HashSet<string> ids)
    {
        var lead = group[0];
        var text = context.Razor.GetValueOrDefault(lead.File) ?? string.Empty;
        var name = NameOf(lead, text);
        return new FeatureFunction
        {
            Id = UniqueId(Slug(group.Count > 1 || lead.Route == "/" ? name : lead.Route), ids),
            Name = name,
            Area = lead.Area,
            Who = WhoOf(lead, text),
            Steps = StepsFor(lead, text, context),
            Routes = [.. group.Select(p => p.Route)],
            Tests = [.. group.SelectMany(p => p.Tests).Select(t => t.File).Distinct().SelectMany(file => context.Classes.Where(c => c.File == file && c.Tests.Count > 0).Select(c => c.Name)).Distinct().Take(MostTests)],
            Notes = "Drafted from the code (page title, link text, form labels): check the name, the steps and who may do it.",
            Generated = true,
        };
    }

    private static string NameOf(PageRow page, string text)
    {
        if (page.Route == "/")
        {
            return "The home page";
        }

        var candidates = new[] { Title().Match(text), Heading().Match(text) }.Where(m => m.Success).Select(m => Tidy(Plain(m.Groups[1].Value)));
        return candidates.FirstOrDefault(c => c.Length > 0 && !c.AsSpan().ContainsAny("{}$\"")) ?? Humanise(Path.GetFileNameWithoutExtension(page.File));
    }

    /// <summary>A title like "Strands - MyWave" is "Strands"; one that starts with the site name's dash is nothing.</summary>
    private static string Tidy(string title)
    {
        var dash = title.IndexOf(" - ", StringComparison.Ordinal);
        return (dash > 0 ? title[..dash] : title).TrimStart('-', ' ');
    }

    private static string WhoOf(PageRow page, string text)
    {
        if (Authorize().Match(text) is not { Success: true } auth)
        {
            return page.SignedIn ? "Signed-in member" : "Anyone";
        }

        return auth.Groups["args"].Value.Contains("Admin", StringComparison.OrdinalIgnoreCase) ? "Admin" : "Signed-in member";
    }

    /// <summary>The clicks from the home page (the real link text on each page on the way) and then what the page itself asks for: its labelled fields and its button.</summary>
    private static string[] StepsFor(PageRow page, string text, DraftContext context)
    {
        var steps = new List<string>();
        foreach (var hop in PathTo(page, context))
        {
            steps.Add(hop);
        }

        steps.AddRange(Label().Matches(text).Select(m => Plain(m.Groups[1].Value)).Where(Usable).Take(MostFormSteps - 1).Select(l => "Type " + l));
        if (Button().Matches(text).Select(m => Plain(m.Groups[1].Value)).FirstOrDefault(Usable) is { } button)
        {
            steps.Add("Click " + button);
        }

        return [.. steps];
    }

    private static IEnumerable<string> PathTo(PageRow page, DraftContext context)
    {
        var hops = new List<string>();
        var current = page;
        while (current.Via is { Length: > 0 } via && via != "-" && hops.Count < 6 && context.Rows.TryGetValue(via, out var parent))
        {
            hops.Insert(0, "Click " + LinkText(context, parent.File, current.Route));
            current = parent;
        }

        return hops;
    }

    /// <summary>The words on the link to <paramref name="route"/>: from the page that links to it, else from the layout or a component that does (the nav menu), else a plain description.</summary>
    private static string LinkText(DraftContext context, string parentFile, string route)
    {
        // A route with a parameter ("/chronicle/{Slug}") is linked as "/chronicle/@x.Slug": match on what comes before the first brace.
        // Links are written with or without the leading slash, and may carry a query or fragment.
        var bare = route.TrimStart('/');
        var brace = bare.IndexOf('{', StringComparison.Ordinal);
        var target = brace < 0 ? "\"/?" + Regex.Escape(bare) + "(?:[?#][^\"]*)?\"" : "\"[^\"]*" + Regex.Escape(bare[..brace]);
        var link = new Regex("<(?:a|NavLink)\\b[^>]*href\\s*=\\s*" + target + "[^>]*>(.*?)</(?:a|NavLink)>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var sources = context.Razor.Where(f => f.Key == parentFile).Concat(context.Razor.Where(f => f.Key != parentFile)).Select(f => f.Value);
        var text = sources.SelectMany(s => link.Matches(s).Select(m => Plain(m.Groups[1].Value))).FirstOrDefault(Usable);
        return text ?? "the link to " + route;
    }

    private static bool Usable(string text) => text.Length is > 0 and <= 40;

    private static string Plain(string markup) => Regex.Replace(Markup().Replace(markup, " "), @"\s+", " ").Trim();

    private static string Humanise(string stem) => Regex.Replace(stem, "(?<=[a-z])(?=[A-Z])", " ").Trim();

    private static string Slug(string text) => Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-') is { Length: > 0 } slug ? slug : "home";

    private static string UniqueId(string slug, HashSet<string> taken)
    {
        var id = slug;
        for (var n = 2; !taken.Add(id); n++)
        {
            id = $"{slug}-{n}";
        }

        return id;
    }
}
