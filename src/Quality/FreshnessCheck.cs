/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Quality;

/// <summary>Decides whether the published copy matches CodeQuality's origin. Pure, so the wording and the exit codes are tested without git.</summary>
internal static class FreshnessCheck
{
    /// <summary>Exit code 0 is current, 1 is not installed or behind, 2 is origin unreadable.</summary>
    public static (int Code, string Message) Compare(InstallState? installed, string? originCommit, int? behind)
    {
        if (installed is null)
        {
            return (1, @"not installed: run dotnet run --project C:\71Degrees\repos\CodeQuality\src\Quality -- install");
        }

        if (string.IsNullOrEmpty(originCommit))
        {
            return (2, "could not read CodeQuality's origin/master");
        }

        if (installed.Commit == originCommit)
        {
            return (0, $"current: {Short(installed.Commit)}, installed {installed.InstalledAt.ToLocalTime():yyyy-MM-dd HH:mm}");
        }

        return (1, $"behind: installed {Short(installed.Commit)}, origin {Short(originCommit)} ({behind?.ToString() ?? "?"} commit(s) newer)");
    }

    /// <summary>The first eight characters of a commit id, enough to recognise it in a message.</summary>
    internal static string Short(string commit) => commit.Length > 8 ? commit[..8] : commit;
}
