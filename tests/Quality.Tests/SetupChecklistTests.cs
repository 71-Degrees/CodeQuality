/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;

namespace CodeQuality.Quality.Tests;

public sealed class SetupChecklistTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quality-setup-" + Guid.NewGuid().ToString("N"));

    public SetupChecklistTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private IReadOnlyList<SetupStatus> Evaluate(ToolConfig? config = null) =>
        SetupChecklist.Evaluate(new SetupFacts(_root, config ?? new ToolConfig(), new Collected(), null, SetupFile.Load(_root)));

    private static SetupState State(IEnumerable<SetupStatus> statuses, string id) => statuses.Single(s => s.Item.Id == id).State;

    [Fact]
    public void ABrandNewSite_HasEverythingApplicableOutstanding_AndNothingIsAssumed()
    {
        var statuses = Evaluate();

        Assert.Equal(SetupState.Missing, State(statuses, "baseline"));
        Assert.Equal(SetupState.Missing, State(statuses, "gate"));
        Assert.Equal(SetupState.Missing, State(statuses, "crawl"));
        Assert.Equal(SetupState.NotApplicable, State(statuses, "scripts")); // no package.json
        Assert.Equal(SetupState.NotApplicable, State(statuses, "release-panel")); // nothing deploys
    }

    [Fact]
    public void EachItem_IsDoneWhenTheRepositoryShowsIt()
    {
        Write("quality/baseline.json", "{ \"Entries\": { \"LongMethod|src/A.cs|A.B(0)\": 70, \"build/CA1305|src/A.cs|(file)\": 2 } }");
        Write(".gitignore", "bin/\nreports/\n");
        Write("tools/Deploy/QualityChecks.cs", "// runs tools/CodeQuality smells");
        Write("Directory.Build.props", "<Project><PropertyGroup><AnalysisMode>Recommended</AnalysisMode></PropertyGroup></Project>");
        Write("quality/features.json", "{}");

        var statuses = Evaluate();

        Assert.All(new[] { "baseline", "analyzer-baseline", "reports-ignored", "gate", "analyzers-on", "features" }, id => Assert.Equal(SetupState.Done, State(statuses, id)));
        Assert.Equal(SetupState.Missing, State(statuses, "tests-coverage"));
    }

    [Fact]
    public void ADeliberateNo_IsRemembered_WithItsReason()
    {
        var file = new SetupFile();
        file.Skipped["crawl"] = new SkipNote("staging is shared with production data", "2026-10-15");

        var statuses = SetupChecklist.Evaluate(new SetupFacts(_root, new ToolConfig(), new Collected(), null, file));

        var crawl = statuses.Single(s => s.Item.Id == "crawl");
        Assert.Equal(SetupState.Skipped, crawl.State);
        Assert.Contains("shared with production", crawl.Note);
        Assert.DoesNotContain(SetupChecklist.Outstanding(statuses), s => s.Item.Id == "crawl");
    }

    [Fact]
    public void WhatNeedsTheOwnersOk_ComesLast_AndFreeValueComesFirst()
    {
        var outstanding = SetupChecklist.Outstanding(Evaluate());

        var firstNeedingOk = outstanding.ToList().FindIndex(s => s.Item.NeedsApproval);
        Assert.True(firstNeedingOk > 0);
        Assert.All(outstanding.Skip(firstNeedingOk), s => Assert.True(s.Item.NeedsApproval));
        Assert.True(outstanding[0].Item.Benefit >= 4); // a high-value, cheap item opens the list
        Assert.Equal("crawl", outstanding[^1].Item.Id);
    }

    [Fact]
    public void ASiteNeverReviewed_GetsTheFirstRunList_ThenOnlyAReminder_ThenNothing()
    {
        var config = new ToolConfig();

        var first = SetupCommand.Notice(_root, config, new Collected());
        Assert.StartsWith("FIRST RUN", first);
        Assert.Contains("Claude: tell the user", first);
        Assert.Contains("NEEDS THE OWNER'S OK", first);

        new SetupFile { FirstRun = "2026-10-15" }.Save(_root);
        var reminder = SetupCommand.Notice(_root, config, new Collected());
        Assert.StartsWith("Setup:", reminder);
        Assert.DoesNotContain("\n", reminder);
        Assert.Contains("baseline", reminder);
    }

    [Fact]
    public void Skipping_NeedsAKnownItemAndAReason()
    {
        var code = SetupCommand.Run(_root, new Options(["--skip", "no-such-item", "--reason", "x"]), new ToolConfig());
        Assert.Equal(2, code);
        Assert.Equal(2, SetupCommand.Run(_root, new Options(["--skip", "crawl"]), new ToolConfig())); // no reason

        Assert.Equal(0, SetupCommand.Run(_root, new Options(["--skip", "crawl", "--reason", "shared data"]), new ToolConfig()));
        Assert.Equal("shared data", SetupFile.Load(_root).Skipped["crawl"].Reason);
        Assert.NotNull(SetupFile.Load(_root).FirstRun);
    }
}
