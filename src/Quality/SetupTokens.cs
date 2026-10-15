/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>How big the site is where it matters for what Claude would have to read: routable pages, Razor markup and test code (bytes).</summary>
internal sealed record SiteSize(int Pages, long RazorBytes, long TestBytes)
{
    /// <summary>Roughly four characters to a token.</summary>
    public long RazorTokens => RazorBytes / 4;

    public long TestTokens => TestBytes / 4;

    internal static SiteSize Measure(string root, ToolConfig config)
    {
        var razor = FeatureMap.Files(root, config.SourceFolders, "*.razor", config).Select(f => new FileInfo(f.Path)).ToList();
        var pages = FeatureMap.Files(root, config.SourceFolders, "*.razor", config).Count(f => File.ReadLines(f.Path).Any(l => l.StartsWith("@page ", StringComparison.Ordinal)));
        var tests = FeatureMap.Files(root, config.TestFolders, "*.cs", config).Sum(f => new FileInfo(f.Path).Length);
        return new SiteSize(pages, razor.Sum(f => f.Length), tests);
    }
}

/// <summary>What Claude would spend on a step: a low and a high estimate in tokens and the reason, so the owner can weigh it against a session instead of guessing from "MEDIUM".</summary>
internal sealed record TokenRange(int Low, int High, string Why)
{
    /// <summary>The yardstick: one 200,000-token context window, the size a long working session fills. The estimates are by hand, scaled to the site's size, not metered.</summary>
    internal const int Session = 200_000;

    internal static TokenRange None(string why) => new(0, 0, why);

    internal static TokenRange Fixed(int low, int high, string why) => new(low, high, why);

    internal Level Level => High == 0 ? Level.None : High < 10_000 ? Level.Low : High < 40_000 ? Level.Medium : Level.High;

    /// <summary>"~37-80k tokens, 18-40% of a session", or "none".</summary>
    internal string Describe() => High == 0
        ? "none"
        : $"~{Thousands(Low)}-{Thousands(High)} tokens, {Percent(Low)}-{Percent(High)}% of a session";

    private static string Thousands(int tokens) => tokens < 1_000 ? tokens.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"{Math.Round(tokens / 1_000.0):0}k";

    private static string Percent(int tokens) => tokens == 0 ? "0" : Math.Max(1, (int)Math.Round(100.0 * tokens / Session)).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
