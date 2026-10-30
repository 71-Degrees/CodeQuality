/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Quality;

/// <summary>
/// What a `dotnet test` run says about itself, for the two ways it can fail: it ran and some tests failed (name them), or it died without a result line (a missing build, a locked file, a crashed host: show its last lines).
/// A bare "Failed!" or "did not complete" used to be all a push or a quick check printed, and the reason was thrown away.
/// </summary>
internal static class TestRunOutput
{
    private const int MostNames = 10;

    /// <summary>The run's own "Passed!" or "Failed!" line; null when it never got as far as running tests.</summary>
    internal static string? SummaryLine(string output) =>
        output.Split('\n').LastOrDefault(l => l.Contains("Passed!", StringComparison.Ordinal) || l.Contains("Failed!", StringComparison.Ordinal))?.Trim();

    /// <summary>The last non-empty lines of the output, for a run that died without a result line.</summary>
    internal static IReadOnlyList<string> LastLines(string output, int count = 8) =>
        output.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).TakeLast(count).ToList();

    /// <summary>The failing tests of a run that did report, each with the first line of its error message when the output has one. Empty when none is named.</summary>
    internal static IReadOnlyList<string> FailedTests(string output)
    {
        var lines = output.Split('\n').Select(l => l.Trim()).ToList();
        var failed = new List<string>();
        for (var i = 0; i < lines.Count && failed.Count < MostNames; i++)
        {
            if (!TryName(lines[i], out var name) || failed.Any(f => f.StartsWith(name, StringComparison.Ordinal)))
            {
                continue;
            }

            var message = lines.Skip(i + 1).Take(6).SkipWhile(l => !l.StartsWith("Error Message", StringComparison.Ordinal)).Skip(1).FirstOrDefault(l => l.Length > 0);
            failed.Add(message is null ? name : $"{name} - {message}");
        }

        return failed;
    }

    // xunit's "[xUnit.net 00:00:16.13]     Ns.Class.Method [FAIL]" and the runner's "Failed Ns.Class.Method [12 ms]".
    private static bool TryName(string line, out string name)
    {
        name = string.Empty;
        if (line.EndsWith("[FAIL]", StringComparison.Ordinal))
        {
            name = line[(line.IndexOf(']') + 1)..].Replace("[FAIL]", string.Empty).Trim();
        }
        else if (line.StartsWith("Failed ", StringComparison.Ordinal) && line.Contains(" [", StringComparison.Ordinal) && !line.Contains('!'))
        {
            name = line["Failed ".Length..line.LastIndexOf(" [", StringComparison.Ordinal)].Trim();
        }

        return name.Length > 0;
    }
}
