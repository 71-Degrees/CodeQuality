/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.Json;
using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>What the page smoke test found for one page: <c>passed</c>, <c>failed</c> or <c>skipped</c> (it needs a record the run cannot make), how long it took to open and what the browser complained about.</summary>
internal sealed record PageSmokeOutcome(string Outcome, long Milliseconds, IReadOnlyList<string> Problems);

/// <summary>Reads the result file the site's page smoke test writes (<c>{ at, pages: [ { route, outcome, milliseconds, problems } ] }</c>), so the feature map can show which pages a real browser opened cleanly.</summary>
internal static class PageSmoke
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static Dictionary<string, PageSmokeOutcome> Read(string root, ToolConfig config)
    {
        var path = Path.Combine(Repo.OutputRoot(root), config.Features?.SmokeResults ?? "reports/browser-data/page-smoke.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : new Dictionary<string, PageSmokeOutcome>(StringComparer.OrdinalIgnoreCase);
    }

    internal static Dictionary<string, PageSmokeOutcome> Parse(string json)
    {
        var found = new Dictionary<string, PageSmokeOutcome>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var file = JsonSerializer.Deserialize<SmokeFile>(json, Json);
            foreach (var page in file?.Pages ?? [])
            {
                found[page.Route] = new PageSmokeOutcome(page.Outcome, page.Milliseconds, page.Problems ?? []);
            }
        }
        catch (JsonException)
        {
            // A half-written or foreign file: the pages show "not run" rather than the map failing.
        }

        return found;
    }

    private sealed record SmokeFile(List<SmokePage>? Pages);

    private sealed record SmokePage(string Route, string Outcome, long Milliseconds, string[]? Problems);
}
