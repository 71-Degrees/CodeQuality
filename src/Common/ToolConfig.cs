/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.Json;

namespace CodeQuality.Common;

/// <summary>The limits above which something is reported. Deliberately plain numbers: they are the conversation starter, tune them in <c>quality/quality.json</c>.</summary>
public sealed class Thresholds
{
    public int MethodLines { get; init; } = 60;

    public int Complexity { get; init; } = 15;

    public int Nesting { get; init; } = 4;

    public int Parameters { get; init; } = 7;

    public int FileLines { get; init; } = 700;

    public int RazorCodeLines { get; init; } = 400;

    /// <summary>A block of code is a duplicate when this many consecutive tokens (names and literals ignored) appear in two places...</summary>
    public int DuplicateTokens { get; init; } = 70;

    /// <summary>...and the repeated run spans at least this many lines.</summary>
    public int DuplicateLines { get; init; } = 8;
}

/// <summary>The site crawler's settings.</summary>
public sealed class CrawlSettings
{
    /// <summary>The address crawled when no <c>--url</c> is given (usually staging). The crawl needs one or the other.</summary>
    public string? DefaultUrl { get; init; }

    public string UserAgent { get; init; } = "CodeQualityCrawler/1.0 (owner-run site check)";

    /// <summary>A regular expression for paths the crawler never visits (sign-in, admin, anything that changes state), on top of static files, which it always skips.</summary>
    public string? SkipPattern { get; init; }
}

/// <summary>
/// What differs from one site to the next, read from <c>quality/quality.json</c> in the repository being checked. Every setting is optional: with no file the tools check <c>src</c> and <c>tools</c>
/// of the solution at the repository root, run no script checks and have no crawl address.
/// </summary>
public sealed class ToolConfig
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>The solution file to build for the analyzers, relative to the root. Default: the only <c>.slnx</c> or <c>.sln</c> there.</summary>
    public string? Solution { get; init; }

    /// <summary>Shown in the report's title. Default: the repository folder's name.</summary>
    public string? SiteName { get; init; }

    public string[] SourceFolders { get; init; } = { "src", "tools" };

    public string[] TestFolders { get; init; } = { "tests" };

    /// <summary>The test projects to run for the test counts and coverage (project files, or folders holding one). Default: every project one folder down under each test folder, except browser tests, which start the site and a browser and run only with --include-browser.</summary>
    public string[]? TestProjects { get; init; }

    /// <summary>Build the solution once before the tests and run every test project with --no-build: a compile error then fails the run in about a minute, and the projects stop each paying for their own build.</summary>
    public bool PrebuildTests { get; init; }

    /// <summary>Run the browser test projects side by side instead of one after another (they each start their own site processes). Needs <see cref="BrowserPrebuild"/> when they share a web project, so two <c>dotnet run</c>s do not build it at once.</summary>
    public bool ParallelBrowserProjects { get; init; }

    /// <summary>Start the browser projects at the same time as the unit projects instead of after them (a trial, 2026-10-27: the browser tests take the longest, so the units hide behind them; the load may flake a browser test).</summary>
    public bool ParallelUnitsWithBrowsers { get; init; }

    /// <summary>Arguments of <c>dotnet</c> commands run once, in order, before the browser projects start (for example building the web project in the configuration the sites run).</summary>
    public string[]? BrowserPrebuild { get; init; }

    /// <summary>The browser tests' run log (Quality.Browser's BrowserRunLog), relative to the root: when set, the report shows each scenario's last time against the one before it and its best.</summary>
    public string? BrowserRunLog { get; init; }

    /// <summary>Folder names to leave out (for example <c>/Migrations/</c>), on top of build output and generated code.</summary>
    public string[] SkipFolders { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Code, tools and tests set aside for now (path fragments like <see cref="SkipFolders"/>, for example a development-only scenario seeder or a load tool): left out of the smell scan, the feature map, test selection and the
    /// test counts until switched back on with <c>--include-parked</c> (or the <see cref="IncludeParkedVariable"/> environment variable). They stay in the build; only the quality tooling looks away.
    /// </summary>
    public string[] Parked { get; init; } = Array.Empty<string>();

    public const string IncludeParkedVariable = "QUALITY_INCLUDE_PARKED";

    /// <summary>Everything the tools skip: the declared folders, and the parked ones unless parking is switched off for this run.</summary>
    public IEnumerable<string> Skipped => SkippedPaths(Environment.GetEnvironmentVariable(IncludeParkedVariable) is { Length: > 0 });

    /// <summary>The skipped path fragments, with or without the parked ones.</summary>
    public IEnumerable<string> SkippedPaths(bool includeParked) => includeParked ? SkipFolders : SkipFolders.Concat(Parked);

    /// <summary>The repo's own npm scripts (<c>typecheck</c>, <c>lint</c>): switched off 2026-11-24 for the machine-wide tools, and run only while those
    /// are not installed.</summary>
    public string[] ScriptChecks { get; init; } = Array.Empty<string>();

    /// <summary>The page globals this repo's browser scripts share (its own namespace, <c>L</c> for Leaflet, <c>Blazor</c>), each <c>writable</c> or
    /// <c>readonly</c>: the one thing the shared lint rule set cannot know.</summary>
    public Dictionary<string, string> ScriptGlobals { get; init; } = new();

    public CrawlSettings Crawl { get; init; } = new();

    /// <summary>The dashboard's release panel: where builds go, what is running there and a button into the deploy console. Absent: no panel.</summary>
    public ReleaseSettings? Release { get; init; }

    /// <summary>Settings for the feature map (<c>quality features</c>): what needs a signed-in visitor. Absent: everything counts as public.</summary>
    public FeatureSettings? Features { get; init; }

    /// <summary>How the generated browser tests start and sign in to the site (<c>quality browser --generate</c>). Absent: nothing can be generated.</summary>
    public BrowserSettings? Browser { get; init; }

    /// <summary>Analyzer rules to leave out of the report, each with the reason, added to the built-in list.</summary>
    public Dictionary<string, string> IgnoredAnalyzerRules { get; init; } = new();

    /// <summary>Smell rules to switch off for this site (for example "DirectClock").</summary>
    public string[] DisabledRules { get; init; } = Array.Empty<string>();

    /// <summary>Per rule, path fragments where it does not apply (the edges of the app, where reading the clock or making an HttpClient is the point): <c>{ "DirectClock": [ "/Startup/" ] }</c>.</summary>
    public Dictionary<string, string[]> RuleExemptions { get; init; } = new();

    /// <summary>How many days of git history the hotspot table looks at.</summary>
    public int HotspotDays { get; init; } = 90;

    public Thresholds Limits { get; init; } = new();

    /// <summary>When a run opens <c>reports/quality-latest.html</c> in the browser by itself: <c>never</c> (the default; <c>--open</c> still does it), <c>always</c> (every run, so the report is in front of the developer
    /// at each iteration) or <c>onFindings</c> (only when something is new or worse, or a check could not run). <c>--no-open</c> overrides it for scripted runs.</summary>
    public string OpenReport { get; init; } = "never";

    /// <summary>How the tools are run from this repository's root, for the "how to run this yourself" panel.</summary>
    public string ToolCommand { get; init; } = "dotnet run --project tools/CodeQuality/src/Quality --";

    /// <summary>Other commands worth listing in that panel, each as "command    what it does".</summary>
    public string[] ExtraCommands { get; init; } = Array.Empty<string>();

    public static string PathFor(string root) => Path.Combine(root, "quality", "quality.json");

    public static ToolConfig Load(string root) =>
        File.Exists(PathFor(root)) ? JsonSerializer.Deserialize<ToolConfig>(File.ReadAllText(PathFor(root)), Json) ?? new ToolConfig() : new ToolConfig();

    public string Title(string root) => string.IsNullOrWhiteSpace(SiteName) ? new DirectoryInfo(root).Name : SiteName;

    /// <summary>The solution to build: the configured one, else the only solution file at the root.</summary>
    public string SolutionPath(string root)
    {
        if (!string.IsNullOrWhiteSpace(Solution))
        {
            return Path.GetFullPath(Solution, root);
        }

        var found = Directory.EnumerateFiles(root, "*.slnx").Concat(Directory.EnumerateFiles(root, "*.sln")).ToList();
        return found.Count == 1 ? found[0] : throw new InvalidOperationException($"Say which solution to build: set \"solution\" in {PathFor(root)} ({found.Count} solution files at the root).");
    }
}
