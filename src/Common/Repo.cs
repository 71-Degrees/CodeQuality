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

    /// <summary>The environment variable that sends a run's outputs (the reports folder, the quality timeline) to another repository root than the one being checked: a background run checks a separate copy of the code but reports into the real working folder.</summary>
    public const string OutputRootVariable = "QUALITY_OUTPUT_ROOT";

    /// <summary>Where a run writes its outputs: the root named by <see cref="OutputRootVariable"/> when it is set and exists, else the repository being checked.</summary>
    public static string OutputRoot(string root) => OutputRoot(root, Environment.GetEnvironmentVariable(OutputRootVariable));

    /// <summary>The same decision with the setting passed in (so it can be tested without touching the environment).</summary>
    public static string OutputRoot(string root, string? configured) => configured is { Length: > 0 } && Directory.Exists(configured) ? configured : root;
}
