/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;
using CodeQuality.Quality;

namespace CodeQuality.Quality.Tests;

public sealed class InstallCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cq-install-" + Guid.NewGuid().ToString("N"));

    public InstallCommandTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static Options OptionsFor(params string[] args) => new(args);

    [Fact]
    public void WithNoArgumentsTheDefaultFoldersApply()
    {
        var home = QualityHome.From(OptionsFor());

        Assert.Equal(QualityHome.DefaultHome, home.Home);
        Assert.Equal(QualityHome.DefaultSource, home.Source);
        Assert.Equal(Path.Combine(QualityHome.DefaultHome, "bin"), home.Bin);
    }

    [Fact]
    public void HomeAndSourceCanBeOverridden()
    {
        var home = QualityHome.From(OptionsFor("--home", Path.Combine(_root, "home"), "--source", Path.Combine(_root, "src")));

        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "home")), home.Home);
        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "src")), home.Source);
    }

    [Fact]
    public void ContainsIsTrueForTheHomeAndItsContentsOnly()
    {
        var home = new QualityHome(Path.Combine(_root, "quality"), @"C:\unused");
        var sibling = Path.Combine(_root, "quality2");

        Assert.True(home.Contains(home.Home));
        Assert.True(home.Contains(Path.Combine(home.Home, "bin")));
        Assert.True(home.Contains(home.Home + Path.DirectorySeparatorChar));
        Assert.True(home.Contains(home.Home.ToUpperInvariant()));
        Assert.False(home.Contains(sibling));
    }

    [Fact]
    public void AnInstallStateRoundTripsThroughItsFile()
    {
        var file = Path.Combine(_root, "installed.json");
        var state = new InstallState("abc123def456", new DateTimeOffset(2026, 11, 24, 1, 45, 0, TimeSpan.Zero), "7.0.2", "10.12.0");

        state.Write(file);

        Assert.Equal(state, InstallState.Read(file));
    }

    [Fact]
    public void AMissingOrUnreadableStateCountsAsNotInstalled()
    {
        var file = Path.Combine(_root, "installed.json");
        Assert.Null(InstallState.Read(file));

        File.WriteAllText(file, "not json");

        Assert.Null(InstallState.Read(file));
    }

    [Fact]
    public void SwapWithNoLiveFolderMovesTheNewBuildIntoPlace()
    {
        var next = MakeFolder("bin.next", "new.txt");
        var live = Path.Combine(_root, "bin");

        Assert.True(FolderSwap.TrySwap(next, live, out var problem));

        Assert.Null(problem);
        Assert.True(File.Exists(Path.Combine(live, "new.txt")));
        Assert.False(Directory.Exists(next));
    }

    [Fact]
    public void SwapKeepsThePreviousCopyBesideTheNewOne()
    {
        var next = MakeFolder("bin.next", "new.txt");
        var live = MakeFolder("bin", "old.txt");

        Assert.True(FolderSwap.TrySwap(next, live, out var problem));

        Assert.Null(problem);
        Assert.True(File.Exists(Path.Combine(live, "new.txt")));
        Assert.False(File.Exists(Path.Combine(live, "old.txt")));
        Assert.True(File.Exists(Path.Combine(live + ".old", "old.txt")));
    }

    [Fact]
    public void SwapReplacesAnOldCopyLeftFromTheLastInstall()
    {
        var next = MakeFolder("bin.next", "new.txt");
        var live = MakeFolder("bin", "old.txt");
        MakeFolder("bin.old", "stale.txt");

        Assert.True(FolderSwap.TrySwap(next, live, out _));

        Assert.True(File.Exists(Path.Combine(live + ".old", "old.txt")));
        Assert.False(File.Exists(Path.Combine(live + ".old", "stale.txt")));
    }

    [Fact]
    public void TheFreshnessCheckSaysWhetherTheCopyIsCurrent()
    {
        var installed = new InstallState("abc123def456", new DateTimeOffset(2026, 11, 24, 1, 45, 0, TimeSpan.Zero), null, null);

        var notInstalled = FreshnessCheck.Compare(null, "abc123def456", 0);
        Assert.Equal(1, notInstalled.Code);
        Assert.StartsWith("not installed", notInstalled.Message);

        var noOrigin = FreshnessCheck.Compare(installed, null, null);
        Assert.Equal(2, noOrigin.Code);
        Assert.StartsWith("could not", noOrigin.Message);

        var current = FreshnessCheck.Compare(installed, "abc123def456", 0);
        Assert.Equal(0, current.Code);
        Assert.StartsWith("current", current.Message);

        var behind = FreshnessCheck.Compare(installed, "999999999aaa", 3);
        Assert.Equal(1, behind.Code);
        Assert.StartsWith("behind", behind.Message);
        Assert.Contains("3 commit(s) newer", behind.Message);
    }

    private string MakeFolder(string name, string file)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, file), name);
        return folder;
    }
}
