/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;
using CodeQuality.Quality;

namespace CodeQuality.Quality.Tests;

public class FeatureDetailsTests
{
    private static readonly Dictionary<string, string> Razor = new()
    {
        ["src/Pages/Host.razor"] = "@page \"/host/{QuizId}\"\n@inject IQuizRepository Quizzes\n@inject NavigationManager Nav\n@inject GameService Games\n<Lobby />",
        ["src/Shared/Lobby.razor"] = "@inject Some.Namespace.IVenueRepository Venues\n@inject IJSRuntime Js\n<Chip />",
        ["src/Shared/Chip.razor"] = "@inject QrCodeService Qr\n<Lobby />",
        ["src/Layout/MainLayout.razor"] = "@inject IUserProfileRepository Profiles",
    };

    private static Dictionary<string, string> ByStem() => Razor.Keys.ToDictionary(f => Path.GetFileNameWithoutExtension(f));

    [Fact]
    public void AFunctionsFilesAreItsPageAndTheComponentsItShows_EvenWhenTheyShowEachOther()
    {
        var files = FeatureDetails.FilesOf("src/Pages/Host.razor", Razor, ByStem());

        Assert.Equal(["src/Pages/Host.razor", "src/Shared/Lobby.razor", "src/Shared/Chip.razor"], files);
    }

    [Fact]
    public void OnlyServicesAndStoresAreListed_NotFrameworkPlumbing_AndNamespacesAreDropped()
    {
        var classes = new Dictionary<string, string> { ["IQuizRepository"] = "Cached" };

        var deps = FeatureDetails.DependenciesOf(FeatureDetails.FilesOf("src/Pages/Host.razor", Razor, ByStem()), Razor, classes);

        Assert.Equal(["GameService", "IQuizRepository", "IVenueRepository", "QrCodeService"], deps.Select(d => d.Name));
        Assert.Equal("Cached", deps.Single(d => d.Name == "IQuizRepository").CacheClass);
        Assert.Null(deps.Single(d => d.Name == "IVenueRepository").CacheClass);
    }

    [Fact]
    public void TheClassOfEachTypeIsReadFromTheTablesOfThePerformanceNotes()
    {
        const string Notes = """
            | Type | Class | Why |
            |---|---|---|
            | `IQuizRepository` | Cached | decorator |
            | `IAudioClipRepository` | HttpCached | files |
            | not a type row | Cached | x |
            """;

        var classes = FeatureDetails.ParseClasses(Notes);

        Assert.Equal("Cached", classes["IQuizRepository"]);
        Assert.Equal("HttpCached", classes["IAudioClipRepository"]);
        Assert.Equal(2, classes.Count);
    }

    [Fact]
    public void ACommitCountsOncePerFunction_NoMatterHowManyOfItsFilesItTouched()
    {
        var changes = FeatureDetails.ParseChanges("@@aaa\nsrc/Pages/Host.razor\nsrc/Shared/Lobby.razor\n\n@@bbb\nsrc/Shared/Lobby.razor\n\n@@ccc\nsrc/Other.razor\n");
        var function = new FeatureFunction { Id = "host", Routes = ["/host/{QuizId}"] };
        var pages = FeatureMap.DiscoverPages(Razor);

        var details = FeatureDetails.For(function, pages, Razor, ByStem(), new Dictionary<string, string>(), changes, new Dictionary<string, string>(), []);

        Assert.Equal(2, details.Changes);
    }

    [Fact]
    public void ADocMentionsAPageOnlyWhenItWritesTheRouteAsCode_AndNotAsPartOfALongerOne()
    {
        var docs = new Dictionary<string, string>
        {
            ["a.md"] = "Open `/quizzes/edit` to build one.",
            ["b.md"] = "The team page is at /team in a sentence.",
            ["c.md"] = "The list is `/quizzes/edit/{id}` for one quiz.",
            ["d.md"] = "Hosted from `/host/session/{code}`.",
        };

        Assert.Equal(["a.md"], FeatureDetails.DocsMentioning(["/quizzes/edit"], docs));
        Assert.Empty(FeatureDetails.DocsMentioning(["/team"], docs));
        Assert.Equal(["d.md"], FeatureDetails.DocsMentioning(["/host/session/{SessionCode}"], docs));
        Assert.Equal(["a.md", "c.md"], FeatureDetails.DocsMentioning(["/quizzes/edit", "/quizzes/edit/{Id}"], docs));
        Assert.Empty(FeatureDetails.DocsMentioning(["/"], docs));
    }

    [Fact]
    public void ADocTheFunctionNamesItselfIsAddedIfItExists_AndIgnoredIfItDoesNot()
    {
        var function = new FeatureFunction { Id = "x", Docs = ["accounts.md", "gone.md"] };
        var docs = new Dictionary<string, string> { ["accounts.md"] = "text" };

        var details = FeatureDetails.For(function, [], Razor, ByStem(), new Dictionary<string, string>(), new Dictionary<string, HashSet<string>>(), docs, []);

        Assert.Equal(["accounts.md"], details.Docs);
    }

    [Fact]
    public void SourceOnASensitivePathIsFlagged()
    {
        var function = new FeatureFunction { Id = "host", Routes = ["/host/{QuizId}"] };
        var pages = FeatureMap.DiscoverPages(Razor);

        var details = FeatureDetails.For(function, pages, Razor, ByStem(), new Dictionary<string, string>(), new Dictionary<string, HashSet<string>>(), new Dictionary<string, string>(), ["Shared/Lobby"]);

        Assert.Equal(["src/Shared/Lobby.razor"], details.Sensitive);
    }
}
