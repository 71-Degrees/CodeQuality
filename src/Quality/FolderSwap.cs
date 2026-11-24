/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Quality;

/// <summary>Replaces a published folder with a freshly built one, keeping the old one until the new one is in place.</summary>
internal static class FolderSwap
{
    /// <summary>Moves next into live's place. Returns false with a problem message when a running tool holds the folder or the move fails.</summary>
    public static bool TrySwap(string next, string live, out string? problem)
    {
        var old = live + ".old";
        var movedAside = false;
        try
        {
            if (Directory.Exists(old))
            {
                Directory.Delete(old, recursive: true);
            }

            if (Directory.Exists(live))
            {
                Directory.Move(live, old);
                movedAside = true;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problem = $"the published copy in {live} is in use (a quality run has it open); the new build is kept in {next}: run install again when it is free";
            return false;
        }

        try
        {
            Directory.Move(next, live);
            problem = null;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (movedAside)
            {
                Directory.Move(old, live);
            }

            problem = $"could not move the new build into {live} ({e.Message}); the previous copy is back in place, and the new build is in {next}";
            return false;
        }
    }
}
