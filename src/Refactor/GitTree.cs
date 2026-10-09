/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;

namespace CodeQuality.Refactor;

/// <summary>Whether the git working tree is clean.</summary>
internal static class GitTree
{
    public static async Task<bool> IsCleanAsync(string root) =>
        string.IsNullOrWhiteSpace((await Shell.RunAsync("git", "status --porcelain", root, 60)).Output);
}
