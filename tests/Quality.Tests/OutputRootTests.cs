/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;

namespace CodeQuality.Quality.Tests;

public sealed class OutputRootTests
{
    [Fact]
    public void OutputsGoToTheRepositoryBeingCheckedUnlessAnotherRootIsNamed()
    {
        var elsewhere = Path.GetTempPath();

        Assert.Equal("/repo", Repo.OutputRoot("/repo", null));
        Assert.Equal("/repo", Repo.OutputRoot("/repo", string.Empty));
        Assert.Equal(elsewhere, Repo.OutputRoot("/repo", elsewhere));
    }

    [Fact]
    public void ARootThatDoesNotExistIsIgnored_SoATypoNeverSendsOutputsNowhere()
    {
        Assert.Equal("/repo", Repo.OutputRoot("/repo", Path.Combine(Path.GetTempPath(), "cq-missing-" + Guid.NewGuid().ToString("N"))));
    }
}
