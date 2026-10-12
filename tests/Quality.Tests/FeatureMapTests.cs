/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Quality;

namespace CodeQuality.Quality.Tests;

public class FeatureMapTests
{
    private static readonly Dictionary<string, string> Site = new()
    {
        ["src/Pages/Home.razor"] = "@page \"/\"\n<a href=\"quizzes\">Quizzes</a> <a href=\"https://elsewhere.example/x\">out</a> <a href=\"#top\">top</a> <Footer />",
        ["src/Pages/Quizzes.razor"] = "@page \"/quizzes\"\n<a href=\"@($\"host/{q.Id}\")\">Host</a> <a href=\"quizzes/edit\">New</a>",
        ["src/Pages/Host.razor"] = "@page \"/host/{QuizId}\"\n@layout Some.Where.HostLayout\n@code { void Go() => Nav.NavigateTo($\"host/session/{code}\"); }",
        ["src/Pages/HostSession.razor"] = "@page \"/host/session/{SessionCode}\"",
        ["src/Pages/Editor.razor"] = "@page \"/quizzes/edit\"\n@page \"/quizzes/edit/{Id}\"",
        ["src/Pages/Orphan.razor"] = "@page \"/orphan\"",
        ["src/Shared/Footer.razor"] = "<a href=\"/terms\">Terms</a>",
        ["src/Pages/Terms.razor"] = "@page \"/terms\"",
        ["src/Layout/MainLayout.razor"] = "<a href=\"\">Home</a> <a href=\"/subscription\">Plans</a>",
        ["src/Layout/HostLayout.razor"] = "<a href=\"quizzes\">Back</a>",
        ["src/Pages/Subscription.razor"] = "@page \"/subscription\"",
    };

    [Fact]
    public void EveryPageDirectiveIsAPage_AFileCanHaveSeveral_AndParametersAreNoted()
    {
        var pages = FeatureMap.DiscoverPages(Site);

        Assert.Contains(pages, p => p.Route == "/quizzes/edit" && !p.Parameters);
        Assert.Contains(pages, p => p.Route == "/quizzes/edit/{Id}" && p.Parameters && p.Area == "quizzes");
        Assert.Contains(pages, p => p.Route == "/" && p.Area == "home");
        Assert.Equal(9, pages.Count);
    }

    [Theory]
    [InlineData("/play/@code", "/play/{}")]
    [InlineData("host/{id}", "/host/{}")]
    [InlineData("quizzes", "/quizzes")]
    [InlineData("", "/")]
    public void LinksAreWrittenTheSameWayWhateverTheirForm(string link, string expected) => Assert.Equal(expected, FeatureMap.Normalize(link));

    [Theory]
    [InlineData("/host/{}", "/host/{QuizId}", true)]
    [InlineData("/host/{}", "/host/session/{SessionCode}", false)]
    [InlineData("/quizzes/edit", "/quizzes/edit", true)]
    [InlineData("/Quizzes", "/quizzes", true)]
    [InlineData("/quizzes", "/quizzes/edit", false)]
    public void ALinkLeadsToTheTemplateItMatches(string link, string route, bool expected) => Assert.Equal(expected, FeatureMap.Leads(link, route));

    [Fact]
    public void LinksToOtherSitesAndPageAnchorsAreNotClicksInsideTheSite()
    {
        var links = FeatureMap.LinksOf("src/Pages/Home.razor", Site, ByStem());

        Assert.Contains("/quizzes", links);
        Assert.DoesNotContain(links, l => l.Contains("elsewhere", StringComparison.Ordinal) || l.Contains("top", StringComparison.Ordinal));
    }

    [Fact]
    public void ALinkInAComponentThePageShowsCountsAsTheirs()
    {
        Assert.Contains("/terms", FeatureMap.LinksOf("src/Pages/Home.razor", Site, ByStem()));
    }

    [Fact]
    public void ClicksAreTheFewestLinksFromHome_ThroughACodeNavigationToo()
    {
        var depth = FeatureMap.Depths(FeatureMap.DiscoverPages(Site), Site);

        Assert.Equal(0, depth["/"].Clicks);
        Assert.Equal(1, depth["/quizzes"].Clicks);
        Assert.Equal(1, depth["/terms"].Clicks);          // through the footer component
        Assert.Equal(1, depth["/subscription"].Clicks);   // through the layout's menu
        Assert.Equal(2, depth["/host/{QuizId}"].Clicks);
        Assert.Equal(3, depth["/host/session/{SessionCode}"].Clicks);
        Assert.Equal("/host/{QuizId}", depth["/host/session/{SessionCode}"].Via);
    }

    [Fact]
    public void APageNothingLinksToHasNoWayIn() => Assert.False(FeatureMap.Depths(FeatureMap.DiscoverPages(Site), Site).ContainsKey("/orphan"));

    [Theory]
    [InlineData("Web.Tests", "tests/Web.Tests/Components/X.cs", "component")]
    [InlineData("Web.Tests", "tests/Web.Tests/PageLoadTests.cs", "unit/web")]
    [InlineData("Browser.Tests", "tests/Browser.Tests/X.cs", "browser")]
    public void ATestIsUnitComponentOrBrowserByWhereItLives(string project, string file, string layer) => Assert.Equal(layer, FeatureMap.LayerOf(project, file));

    [Fact]
    public void TheAreaIsTheFirstSegment() => Assert.Equal("admin", FeatureMap.AreaOf("/admin/users"));

    private static Dictionary<string, string> ByStem() => Site.Keys.GroupBy(f => Path.GetFileNameWithoutExtension(f)).ToDictionary(g => g.Key, g => g.First());
}
