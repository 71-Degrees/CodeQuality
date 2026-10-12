/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.RegularExpressions;

namespace CodeQuality.Browser;

/// <summary>What counts as a problem worth failing a browser test for, and what is noise. A console error, an uncaught page error and a server error response are problems; the rest is not.</summary>
public sealed class ProbeFilter
{
    private readonly List<Regex> _ignored = [];

    /// <summary>Messages to leave out, as regular expressions over the message text (for example a known third-party warning). Each should carry a comment saying why.</summary>
    public ProbeFilter Ignore(string pattern)
    {
        _ignored.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)));
        return this;
    }

    public bool IsIgnored(string message) => _ignored.Exists(r => r.IsMatch(message));

    /// <summary>Whether a browser console message of this type is a problem. Only errors are: warnings and logs are how the page talks.</summary>
    public static bool IsConsoleProblem(string type) => string.Equals(type, "error", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a response is a problem: the server failed (5xx). A 404 for a missing favicon or a 401 for a signed-out visitor is the site working as designed.</summary>
    public static bool IsResponseProblem(int status) => status >= 500;
}
