/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Quality;

/// <summary>Where the machine-wide copy of CodeQuality is published, and the source checkout it is built from.</summary>
internal sealed record QualityHome(string Home, string Source)
{
    /// <summary>Outside AppData on purpose: the Claude desktop app redirects writes there.</summary>
    public const string DefaultHome = @"C:\71Degrees\published\quality";

    public const string DefaultSource = @"C:\71Degrees\repos\CodeQuality";

    public string Bin => Path.Combine(Home, "bin");

    public string Refactor => Path.Combine(Home, "refactor");

    public string Node => Path.Combine(Home, "node");

    public string InstalledFile => Path.Combine(Home, "installed.json");

    public static QualityHome From(Options options) => new(
        Path.GetFullPath(options.Text("--home", DefaultHome)),
        Path.GetFullPath(options.Text("--source", DefaultSource)));

    /// <summary>True when the folder is the published copy or inside it. A running copy must not be replaced, so install checks this first.</summary>
    public bool Contains(string folder)
    {
        var full = TrimEnd(Path.GetFullPath(folder));
        var home = TrimEnd(Home);
        return string.Equals(full, home, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(home + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimEnd(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
