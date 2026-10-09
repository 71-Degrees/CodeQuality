/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.RegularExpressions;
using CodeQuality.Common;

namespace CodeQuality.Refactor;

/// <summary>One analyzer warning from the build, where the compiler says it is (1-based line and column).</summary>
internal sealed record Warning(string Rule, string File, int Line, int Column, string Message);

/// <summary>Reads the .NET analyzer warnings out of a full build. The analyzers run at Recommended level in every build (Directory.Build.props), so a plain rebuild reports them.</summary>
internal static partial class BuildWarnings
{
    [GeneratedRegex(@"^(?<file>[A-Za-z]:[^()]*?)\((?<line>\d+),(?<col>\d+)\): warning (?<rule>[A-Z]+\d+): (?<message>.*?) \[", RegexOptions.Multiline)]
    private static partial Regex Pattern();

    public static async Task<IReadOnlyList<Warning>> CollectAsync(string root, string solution)
    {
        // --no-incremental: an incremental build skips the compiler, and a skipped compiler reports no warnings.
        var (_, output) = await Shell.RunAsync("dotnet", $"build \"{solution}\" --no-incremental -nologo -v:q", root);
        return Parse(output);
    }

    /// <summary>The distinct warnings in build output (a project built for two targets prints each one twice).</summary>
    public static IReadOnlyList<Warning> Parse(string output) =>
        Pattern().Matches(output)
            .Select(m => new Warning(m.Groups["rule"].Value, m.Groups["file"].Value.Trim(), int.Parse(m.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(m.Groups["col"].Value, System.Globalization.CultureInfo.InvariantCulture), m.Groups["message"].Value))
            .Distinct()
            .ToList();
}
