/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Microsoft.Playwright;

namespace CodeQuality.Browser;

/// <summary>Playwright's Chromium: whether it is installed here, and a way to launch it. Install it once with <c>playwright.ps1 install chromium</c> from a built Playwright project (see README.md).</summary>
public static class Chromium
{
    /// <summary>Set to anything but <c>0</c> to watch the browser work (headed, slowed down) instead of running it hidden.</summary>
    public const string HeadedVariable = "QUALITY_BROWSER_HEADED";

    /// <summary>Set to <c>0</c> to skip every browser test (a machine without a browser, a quick run).</summary>
    public const string EnabledVariable = "QUALITY_BROWSER";

    /// <summary>
    /// Chromium's built-in stand-in camera (a moving test pattern) with the permission prompt answered yes: a page that opens the camera can be driven like a phone
    /// with a real one, so a black or frozen viewfinder can be caught by a test instead of on the owner's phone.
    /// </summary>
    public static readonly IReadOnlyList<string> FakeCameraArguments = ["--use-fake-device-for-media-stream", "--use-fake-ui-for-media-stream"];

    private static readonly Lazy<string?> WhyNot = new(Check);

    /// <summary>Why browser tests cannot run here, or null when they can.</summary>
    public static string? WhyUnavailable() => WhyNot.Value;

    /// <summary>
    /// Starts Playwright and a Chromium. The caller disposes both. <paramref name="arguments"/> are extra Chromium switches a site's tests need, for example
    /// <see cref="FakeCameraArguments"/> for a page that opens the camera.
    /// </summary>
    public static async Task<(IPlaywright Playwright, IBrowser Browser)> LaunchAsync(IEnumerable<string>? arguments = null)
    {
        var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
        var headed = Environment.GetEnvironmentVariable(HeadedVariable) is { Length: > 0 } value && value != "0";
        var options = new BrowserTypeLaunchOptions { Headless = !headed, SlowMo = headed ? 250 : 0, Args = arguments?.ToList() };
        var browser = await playwright.Chromium.LaunchAsync(options).ConfigureAwait(false);
        return (playwright, browser);
    }

    // A look at the folder Playwright installs its browsers into, rather than starting Playwright: the answer is wanted synchronously (a test attribute cannot await), and a folder that is there is what "installed" means.
    private static string? Check()
    {
        if (Environment.GetEnvironmentVariable(EnabledVariable) == "0")
        {
            return $"browser tests are switched off ({EnabledVariable}=0)";
        }

        var root = BrowsersFolder();
        var installed = root is not null && Directory.Exists(root) && Directory.EnumerateDirectories(root, "chromium*").Any();
        return installed ? null : $"Chromium is not installed (looked in {root ?? "an unknown folder"}; install it once with playwright.ps1 install chromium)";
    }

    private static string? BrowsersFolder()
    {
        var configured = Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && configured != "0")
        {
            return configured;
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ms-playwright");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return OperatingSystem.IsMacOS() ? Path.Combine(home, "Library", "Caches", "ms-playwright") : Path.Combine(home, ".cache", "ms-playwright");
    }
}
