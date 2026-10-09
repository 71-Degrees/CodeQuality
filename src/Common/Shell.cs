/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Diagnostics;
using System.Text;

namespace CodeQuality.Common;

/// <summary>Runs outside programs for the console tools.</summary>
public static class Shell
{
    /// <summary>Runs a program to the end and returns its exit code and combined output. Standard input is closed so nothing waits for a keypress.</summary>
    public static async Task<(int Code, string Output)> RunAsync(string fileName, string arguments, string workingDirectory, int seconds = 900)
    {
        var info = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start " + fileName);
        process.StandardInput.Close();
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => Collect(output, e.Data);
        process.ErrorDataReceived += (_, e) => Collect(output, e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return (-1, output + $"\n(stopped after {seconds} s)");
        }

        return (process.ExitCode, output.ToString());
    }

    private static void Collect(StringBuilder output, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (output)
        {
            output.AppendLine(line);
        }
    }
}
