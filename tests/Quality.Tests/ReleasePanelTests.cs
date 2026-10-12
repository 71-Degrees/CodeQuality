/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;
using CodeQuality.Quality;

namespace CodeQuality.Quality.Tests;

public class ReleasePanelTests
{
    private sealed class FakeGit(string head, Dictionary<(string From, string To), string[]> changes) : IGitView
    {
        public Task<string> HeadAsync() => Task.FromResult(head);

        public Task<int> CommitsBetweenAsync(string from, string to) =>
            Task.FromResult(from == to ? 0 : changes.ContainsKey((from, to)) ? changes[(from, to)].Length + 1 : -1);

        public Task<IReadOnlyList<string>> FilesBetweenAsync(string from, string to) => Task.FromResult<IReadOnlyList<string>>(changes.GetValueOrDefault((from, to), []));
    }

    private static readonly ReleaseSettings Settings = new()
    {
        ConsoleUrl = "http://127.0.0.1:5098",
        SensitivePaths = ["Accounts/", "Billing"],
        Targets =
        [
            new ReleaseTarget { Name = "Staging", VersionUrl = "https://stage/version", Button = "Deploy to staging" },
            new ReleaseTarget { Name = "Production", VersionUrl = "https://prod/version", PromotesFrom = "Staging", Button = "Deploy to production", WarnOnSensitive = true },
        ],
    };

    private static Task<string?> Versions(string url) => Task.FromResult<string?>(url.Contains("stage", StringComparison.Ordinal) ? """{"version":"1.0.0+bbbb"}""" : """{"version":"1.0.0+aaaa"}""");

    [Theory]
    [InlineData("""{"version":"1.0.0+4c8ee4e9","environment":null}""", "4c8ee4e9")]
    [InlineData("""{"version":"1.0.0"}""", null)]
    [InlineData("""{"version":"1.0.0+"}""", null)]
    [InlineData("not json", null)]
    [InlineData(null, null)]
    public void TheBuildIsTheCommitAfterThePlus(string? json, string? expected) => Assert.Equal(expected, ReleasePanel.ParseBuild(json));

    [Fact]
    public void OnlyFilesOnASensitivePathAreFlagged_IgnoringCase()
    {
        var found = ReleasePanel.Sensitive(["src/Core/accounts/Role.cs", "src/Web/Pages/Home.razor", "src/Web/Services/Billing/Stripe.cs", "tests/Core.Tests/Accounts/RoleTests.cs"], ["Accounts/", "Billing"]);

        Assert.Equal(["src/Core/accounts/Role.cs", "src/Web/Services/Billing/Stripe.cs"], found);
    }

    [Fact]
    public async Task StagingIsComparedWithTheLatestCommit_ProductionWithWhatStagingRuns()
    {
        var git = new FakeGit("cccc", new()
        {
            [("bbbb", "cccc")] = ["src/Web/Home.razor"],
            [("aaaa", "bbbb")] = ["src/Core/Accounts/Role.cs", "src/Web/Other.cs"],
        });

        var panel = await ReleasePanel.BuildAsync(Settings, Versions, git);

        Assert.Equal(("bbbb", 2, "the latest commit"), (panel[0].Build, panel[0].CommitsAhead, panel[0].Source));
        Assert.Empty(panel[0].Sensitive);
        Assert.Equal(("aaaa", 3, "Staging's build"), (panel[1].Build, panel[1].CommitsAhead, panel[1].Source));
        Assert.Equal(["src/Core/Accounts/Role.cs"], panel[1].Sensitive);
        Assert.True(panel[1].Warn);
    }

    [Fact]
    public async Task ATargetThatIsUpToDateSaysSo()
    {
        var git = new FakeGit("bbbb", new());

        var panel = await ReleasePanel.BuildAsync(Settings, url => Task.FromResult<string?>("""{"version":"1.0.0+bbbb"}"""), git);

        Assert.All(panel, t => Assert.Equal(0, t.CommitsAhead));
        Assert.All(panel, t => Assert.Null(t.Problem));
    }

    [Fact]
    public async Task ASiteThatDoesNotAnswerIsUnknownNotAnError()
    {
        var panel = await ReleasePanel.BuildAsync(Settings, _ => Task.FromResult<string?>(null), new FakeGit("cccc", new()));

        Assert.All(panel, t => Assert.NotNull(t.Problem));
        Assert.All(panel, t => Assert.Null(t.Build));
    }

    [Fact]
    public async Task ABuildGitHasNeverSeenIsReportedInsteadOfShownAsNothingToDeploy()
    {
        var panel = await ReleasePanel.BuildAsync(Settings, Versions, new FakeGit("cccc", new()));

        Assert.Contains("Git does not know", panel[0].Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDashboardCarriesTheReleasePanelAndTheConsoleAddress()
    {
        var status = new TargetStatus("Production", "Deploy to production", "aaaa", "Staging's build", 3, 4, ["src/Billing.cs"], true, null);

        var html = Dashboard.Render("Site", "abc", new History(), [], [status], "http://127.0.0.1:5098");

        Assert.Contains("\"consoleUrl\":\"http://127.0.0.1:5098\"", html, StringComparison.Ordinal);
        Assert.Contains("\"button\":\"Deploy to production\"", html, StringComparison.Ordinal);
        Assert.Contains("src/Billing.cs", html, StringComparison.Ordinal);
    }
}
