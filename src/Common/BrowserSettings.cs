/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Common;

/// <summary>What <c>quality browser --generate</c> needs to know to start the site and sign in: the <c>browser</c> block of quality/quality.json.</summary>
public sealed class BrowserSettings
{
    /// <summary>The web project the browser tests start (a .csproj, relative to the root).</summary>
    public string? WebProject { get; init; }

    /// <summary>The folder of the generated test project. Default: <c>tests/&lt;SiteName&gt;.Browser.Tests</c>.</summary>
    public string? TestProject { get; init; }

    /// <summary>The address (relative) that answers 200 once the site is up.</summary>
    public string ReadyPath { get; init; } = "/healthz";

    /// <summary>An address that signs a test visitor in without a real sign-in (the site's development seed shortcut, for example <c>/dev/seed?as=member</c>). Absent: every function is visited as a stranger.</summary>
    public string? SignInPath { get; init; }

    /// <summary>Settings for the site process, so a test run never reaches an outside service.</summary>
    public Dictionary<string, string> Environment { get; init; } = new();
}
