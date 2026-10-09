/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Common;

/// <summary>Finds the repository a tool is running in.</summary>
public static class Repo
{
    /// <summary>The repository root: the nearest folder above the working directory that holds a <c>.git</c> folder (or file, for a worktree or a submodule).</summary>
    public static string FindRoot()
    {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Run this from inside the repository you want to check (the working directory must be in a git repository).");
    }
}
