/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;

namespace CodeQuality.Quality.Tests;

public sealed class BrowserGeneratorTests
{
    private static FeatureFunction Function(string id, string who, string[] steps, string[] routes) => new() { Id = id, Name = id, Who = who, Steps = steps, Routes = routes };

    private static string Journeys(params FeatureFunction[] functions) =>
        BrowserGenerator.Files("My Site", new BrowserSettings { WebProject = "src/Web/Web.csproj", SignInPath = "/dev/seed?as=admin" }, new FeatureRegistryFile { Functions = functions }, "..\\..\\tools\\CodeQuality", "src/Web/Web.csproj")["Journeys.cs"];

    [Fact]
    public void AFunctionWithClickSteps_FollowsThemFromTheHomePage_AndFallsBackToItsFirstFixedPage()
    {
        var text = Journeys(Function("read-legal", "Anyone", ["Click Privacy Policy"], ["/terms", "/privacy"]));

        Assert.Contains("public async Task read_legal()", text, StringComparison.Ordinal);
        Assert.Contains("Journey.FollowAsync(site, page, \"/terms\", \"Privacy Policy\")", text, StringComparison.Ordinal);
        Assert.Contains("Journey.AssertOnOneOf(page, \"/terms\", \"/privacy\")", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SignInAsync", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AFunctionTheSiteWalksItself_RunsThatWalk_EvenWhenItsPageNeedsARecord()
    {
        var files = BrowserGenerator.Files("My Site", new BrowserSettings { SignInPath = "/dev/seed" },
            new FeatureRegistryFile { Functions = [Function("join", "Anyone", ["Click Join", "Type a code"], ["/play/{Code}"]), Function("read", "Anyone", ["Click Terms"], ["/terms"])] },
            "..\\..\\tools\\CodeQuality", "src/Web/Web.csproj", "// [\"read\"] = nothing yet\n            [\"join\"] = JoinAsync,");

        var text = files["Journeys.cs"];
        Assert.Contains("Walks.ByFunction[\"join\"](site, page)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Walks.ByFunction[\"read\"]", text, StringComparison.Ordinal);   // a commented-out entry is not a walk
        Assert.Contains("public async Task read()", text, StringComparison.Ordinal);
        Assert.Contains("class Walks", files["Walks.cs"], StringComparison.Ordinal);
        Assert.Contains("ClickAsync", files["Journey.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void ClicksStopAtTheFirstEntry_BecauseTheJourneyDrivesNavigationNotForms()
    {
        var path = BrowserGenerator.ClickPath(Function("f", "Anyone", ["Click Contact", "Type Message", "Click Send"], ["/contact"]));

        Assert.Equal(["Contact"], path);
    }

    [Fact]
    public void ASignedInFunctionSignsInFirst_AndOneWithOnlyRecordPagesGetsNoTest()
    {
        var text = Journeys(
            Function("your-wave", "Signed-in member", ["Click Open your wave"], ["/wave"]),
            Function("view-one", "Anyone", ["Click Open"], ["/w/{Id}"]));

        Assert.Contains("Journey.SignInAsync(site, journey.Page)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("view_one()", text, StringComparison.Ordinal);
        Assert.Contains("No test (only pages with a route parameter", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheQuickFilterNamesTheJourneyOfEachSelectedFunction()
    {
        var filter = BrowserCommand.Filter([Function("read-legal", "Anyone", [], []), Function("home", "Anyone", [], [])]);

        Assert.Equal("FullyQualifiedName~Journeys.read_legal|FullyQualifiedName~Journeys.home", filter);
    }

    [Fact]
    public void RouteParametersAreCutOffSoAnyRecordSatisfiesThePage()
    {
        Assert.Equal("/chronicle/", BrowserGenerator.Base("/chronicle/{Slug}/map"));
        Assert.Equal("/wave", BrowserGenerator.Base("/wave"));
    }

    [Fact]
    public void TheCsprojCommentHasNoDoubleHyphen_BecauseXmlForbidsIt()
    {
        var project = BrowserGenerator.Files("My Site", new BrowserSettings(), new FeatureRegistryFile(), "..\\..\\tools\\CodeQuality", "src/Web/Web.csproj")["MySite.Browser.Tests.csproj"];

        var comment = project[(project.IndexOf("<!--", StringComparison.Ordinal) + 4)..project.IndexOf("-->", StringComparison.Ordinal)];
        Assert.DoesNotContain("--", comment, StringComparison.Ordinal);
    }
}
