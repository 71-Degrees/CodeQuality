/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>Starts the live view of a run: the page and status files, and (for a person at the keyboard) the page open in Chrome. <c>--no-live</c> turns it off, <c>--no-open</c> keeps the files but opens nothing (the push gate and background run use it).</summary>
internal static class LiveSession
{
    internal static LiveRun Begin(string root, string command, Options options)
    {
        if (options.Flag("--no-live"))
        {
            return LiveRun.Off;
        }

        var reports = Path.Combine(Repo.OutputRoot(root), "reports");
        var files = new LiveFiles(reports);
        try
        {
            files.WritePage();
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine("  (no live page this run: " + ex.Message + ")");
            return LiveRun.Off;
        }

        // The first snapshot is written before the page opens, so it never shows the run before this one as finished.
        var run = new LiveRun(command, files, StageEstimates.Load(reports), () => DateTimeOffset.Now);
        Console.WriteLine($"Live view: {new Uri(files.PagePath).AbsoluteUri}");
        if (!options.Flag("--no-open"))
        {
            HtmlReport.Open(files.PagePath, force: true);
        }

        return run;
    }
}
