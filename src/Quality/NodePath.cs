/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Quality;

/// <summary>Puts Node on the PATH for the child processes that run npm and the script checks.</summary>
internal static class NodePath
{
    /// <summary>A scheduled task's PATH may lack Node, so the default install folder goes first when it exists.</summary>
    public static void Ensure()
    {
        var nodeDir = new[] { Environment.GetEnvironmentVariable("ProgramFiles") + @"\nodejs" }.FirstOrDefault(Directory.Exists);
        var path = nodeDir is null ? Environment.GetEnvironmentVariable("PATH") : nodeDir + ";" + Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", path);
    }
}
