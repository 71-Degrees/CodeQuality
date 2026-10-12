/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace CodeQuality.Browser;

/// <summary>How to start a site for a browser test. Everything here is the site's own: the tools know nothing about what the site is.</summary>
public sealed class SiteHostOptions
{
    /// <summary>The web project to run (a .csproj path).</summary>
    public required string ProjectPath { get; init; }

    /// <summary>The build configuration to run. A separate one from the everyday Debug (the site's own test configuration) means a developer's running dev server, which holds the normal build output, never blocks it.</summary>
    public string? Configuration { get; init; }

    /// <summary>The address (relative) that answers 200 once the site is ready to serve.</summary>
    public string ReadyPath { get; init; } = "/healthz";

    /// <summary>Environment variables for the site process (settings, storage folders, switches that keep a test from reaching outside services).</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    /// <summary>Extra arguments passed to the site itself.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(180);
}

/// <summary>The site, running as its own process on a free local port for the length of a test run. Disposing it stops the process; it never touches the site's data.</summary>
public sealed class SiteHost : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Queue<string> _output = new();

    private SiteHost(Process process, string baseUrl)
    {
        _process = process;
        BaseUrl = baseUrl;
    }

    /// <summary>For example <c>http://127.0.0.1:53211</c>, without a trailing slash.</summary>
    public string BaseUrl { get; }

    /// <summary>How long the site took to build and become ready: a number worth watching from run to run.</summary>
    public TimeSpan StartedIn { get; private set; }

    /// <summary>The last lines the site wrote, for explaining a failure.</summary>
    public string RecentOutput
    {
        get
        {
            lock (_output)
            {
                return string.Join(System.Environment.NewLine, _output);
            }
        }
    }

    public static async Task<SiteHost> StartAsync(SiteHostOptions options)
    {
        var started = Stopwatch.StartNew();
        var baseUrl = $"http://127.0.0.1:{FreePort()}";
        var process = StartProcess(options, baseUrl);
        var host = new SiteHost(process, baseUrl);
        try
        {
            process.OutputDataReceived += (_, e) => host.Keep(e.Data);
            process.ErrorDataReceived += (_, e) => host.Keep(e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await WaitUntilReadyAsync(baseUrl + options.ReadyPath, options.StartupTimeout, () => process.HasExited).ConfigureAwait(false);
            host.StartedIn = started.Elapsed;
            return host;
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException)
        {
            var output = host.RecentOutput;
            Stop(process);
            throw new InvalidOperationException($"{ex.Message}{System.Environment.NewLine}Recent output from the site:{System.Environment.NewLine}{output}", ex);
        }
    }

    /// <summary>Polls the address until it answers 200, the process dies, or the time is up.</summary>
    internal static async Task WaitUntilReadyAsync(string url, TimeSpan timeout, Func<bool> hasExited)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (hasExited())
            {
                throw new InvalidOperationException("The site stopped before it became ready.");
            }

            if (await AnswersOkAsync(http, url).ConfigureAwait(false))
            {
                return;
            }

            await Task.Delay(500).ConfigureAwait(false);
        }

        throw new TimeoutException($"The site did not answer {url} within {timeout.TotalSeconds:0} seconds.");
    }

    private static async Task<bool> AnswersOkAsync(HttpClient http, string url)
    {
        try
        {
            using var response = await http.GetAsync(new Uri(url)).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;   // not listening yet: that is what the polling is for
        }
    }

    internal static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static Process StartProcess(SiteHostOptions options, string baseUrl)
    {
        var arguments = new List<string> { "run", "--project", options.ProjectPath, "--no-launch-profile" };
        if (!string.IsNullOrWhiteSpace(options.Configuration))
        {
            arguments.AddRange(["-c", options.Configuration]);
        }

        arguments.AddRange(["--urls", baseUrl, "--"]);
        arguments.AddRange(options.Arguments);

        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in options.Environment)
        {
            info.Environment[key] = value;
        }

        return Process.Start(info) ?? throw new InvalidOperationException("Could not start the site (dotnet run).");
    }

    private void Keep(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_output)
        {
            _output.Enqueue(line);
            while (_output.Count > 60)
            {
                _output.Dequeue();
            }
        }
    }

    private static void Stop(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }

    public ValueTask DisposeAsync()
    {
        Stop(_process);
        _process.Dispose();
        return ValueTask.CompletedTask;
    }
}
