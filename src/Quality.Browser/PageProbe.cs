/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Microsoft.Playwright;

namespace CodeQuality.Browser;

/// <summary>
/// Listens to one page while a test drives it and keeps what went wrong: console errors, uncaught page errors and server errors. A browser test calls <see cref="AssertClean"/> at the end (or after each step),
/// so a circuit that crashed or a script that threw fails the test even when the visible assertions happened to pass.
/// </summary>
public sealed class PageProbe
{
    private readonly List<string> _problems = [];
    private readonly ProbeFilter _filter;

    public PageProbe(IPage page, ProbeFilter? filter = null)
    {
        _filter = filter ?? new ProbeFilter();
        page.Console += (_, message) => Record(ProbeFilter.IsConsoleProblem(message.Type), $"console {message.Type}: {message.Text}");
        page.PageError += (_, text) => Record(true, $"page error: {text}");
        page.Response += (_, response) => Record(ProbeFilter.IsResponseProblem(response.Status), $"HTTP {response.Status}: {response.Url}");
    }

    /// <summary>Everything that went wrong so far.</summary>
    public IReadOnlyList<string> Problems
    {
        get
        {
            lock (_problems)
            {
                return [.. _problems];
            }
        }
    }

    /// <summary>Forgets what was recorded so far (a test that deliberately provoked an error, before the part that must stay clean).</summary>
    public void Clear()
    {
        lock (_problems)
        {
            _problems.Clear();
        }
    }

    /// <summary>Fails the test, listing every problem, if there were any.</summary>
    public void AssertClean()
    {
        var problems = Problems;
        if (problems.Count > 0)
        {
            throw new BrowserProblemException("The page had problems:" + Environment.NewLine + string.Join(Environment.NewLine, problems.Select(p => "  - " + p)));
        }
    }

    private void Record(bool isProblem, string text)
    {
        if (!isProblem || _filter.IsIgnored(text))
        {
            return;
        }

        lock (_problems)
        {
            _problems.Add(text);
        }
    }
}

/// <summary>A browser test failed because the page logged an error, threw, or the server failed.</summary>
public sealed class BrowserProblemException(string message) : Exception(message);
