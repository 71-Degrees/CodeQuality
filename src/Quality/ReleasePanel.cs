/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.Json;
using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>One target as the panel shows it.</summary>
internal sealed record TargetStatus(string Name, string Button, string? Build, string? Source, int CommitsAhead, int FilesChanged, IReadOnlyList<string> Sensitive, bool Warn, string? Problem);

/// <summary>The two git questions the panel asks, so they can be answered by a fake in tests.</summary>
internal interface IGitView
{
    Task<string> HeadAsync();

    Task<int> CommitsBetweenAsync(string from, string to);

    Task<IReadOnlyList<string>> FilesBetweenAsync(string from, string to);
}

internal sealed class RepoGit(string root) : IGitView
{
    public async Task<string> HeadAsync() => (await Shell.RunAsync("git", "rev-parse --short=8 HEAD", root, 30)).Output.Trim();

    public async Task<int> CommitsBetweenAsync(string from, string to)
    {
        var result = await Shell.RunAsync("git", $"rev-list --count {from}..{to}", root, 30);
        return result.Code == 0 && int.TryParse(result.Output.Trim(), out var count) ? count : -1;
    }

    public async Task<IReadOnlyList<string>> FilesBetweenAsync(string from, string to)
    {
        var result = await Shell.RunAsync("git", $"diff --name-only {from} {to}", root, 30);
        return result.Code == 0 ? result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
    }
}

internal static class ReleasePanel
{
    /// <summary>The commit in a version string such as <c>{"version":"1.0.0+4c8ee4e9"}</c>, or null when the text has none.</summary>
    public static string? ParseBuild(string? versionJson)
    {
        if (string.IsNullOrWhiteSpace(versionJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(versionJson);
            var version = doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
            var plus = version?.IndexOf('+', StringComparison.Ordinal) ?? -1;
            return plus >= 0 && plus + 1 < version!.Length ? version[(plus + 1)..] : null;
        }
        catch (JsonException)
        {
            return null;   // not a version document: shown as "unknown"
        }
    }

    /// <summary>The files whose path contains one of the fragments, in the order given. Tests are left out: they change what is checked, not what the site does.</summary>
    public static IReadOnlyList<string> Sensitive(IEnumerable<string> files, IEnumerable<string> fragments)
    {
        var wanted = fragments.ToList();
        return files.Where(f => !f.StartsWith("tests/", StringComparison.OrdinalIgnoreCase) && wanted.Any(w => f.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    public static async Task<List<TargetStatus>> BuildAsync(ReleaseSettings settings, Func<string, Task<string?>> fetchVersion, IGitView git)
    {
        var builds = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in settings.Targets)
        {
            builds[target.Name] = ParseBuild(await fetchVersion(target.VersionUrl));
        }

        var head = await git.HeadAsync();
        var statuses = new List<TargetStatus>();
        foreach (var target in settings.Targets)
        {
            statuses.Add(await StatusOfAsync(target, builds, head, settings, git));
        }

        return statuses;
    }

    private static async Task<TargetStatus> StatusOfAsync(ReleaseTarget target, Dictionary<string, string?> builds, string head, ReleaseSettings settings, IGitView git)
    {
        var running = builds[target.Name];
        var source = string.IsNullOrWhiteSpace(target.PromotesFrom) ? head : builds.GetValueOrDefault(target.PromotesFrom);
        var sourceName = string.IsNullOrWhiteSpace(target.PromotesFrom) ? "the latest commit" : target.PromotesFrom + "'s build";
        if (running is null || source is null)
        {
            return new TargetStatus(target.Name, target.Button, running, sourceName, 0, 0, [], target.WarnOnSensitive, running is null ? "Could not read the running build." : $"Could not read {target.PromotesFrom}'s build.");
        }

        var commits = await git.CommitsBetweenAsync(running, source);
        var files = commits > 0 ? await git.FilesBetweenAsync(running, source) : [];
        return new TargetStatus(target.Name, target.Button, running, sourceName, Math.Max(commits, 0), files.Count, Sensitive(files, settings.SensitivePaths), target.WarnOnSensitive, commits < 0 ? "Git does not know that build here (fetch or pull?)." : null);
    }
}
