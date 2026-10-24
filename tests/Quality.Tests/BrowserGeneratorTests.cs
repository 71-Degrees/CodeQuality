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

    [Fact]
    public void WithShards_EveryJourneyIsInExactlyOneClass_ThatHasASiteOfItsOwn_AndTheFilterFindsIt()
    {
        var functions = Enumerable.Range(0, 12).Select(i => Function("page-" + i, "Anyone", ["Click Page"], ["/p" + i])).ToArray();
        var text = BrowserGenerator.Files("My Site", new BrowserSettings { WebProject = "src/Web/Web.csproj", Shards = 3 }, new FeatureRegistryFile { Functions = functions }, "../../tools/CodeQuality", "src/Web/Web.csproj")["Journeys.cs"];

        Assert.Contains("public sealed class Journeys(Site site) : IClassFixture<Site>", text, StringComparison.Ordinal);
        Assert.Contains("public sealed class SiteB : Site", text, StringComparison.Ordinal);
        Assert.Contains("protected override string Shard => \"c\";", text, StringComparison.Ordinal);
        Assert.Contains("public sealed class JourneysC(SiteC site) : IClassFixture<SiteC>", text, StringComparison.Ordinal);
        foreach (var function in functions)
        {
            var method = BrowserGenerator.MethodName(function.Id);
            Assert.Equal(1, text.Split($"public async Task {method}()", StringSplitOptions.None).Length - 1);
            Assert.Contains($"FullyQualifiedName~{BrowserGenerator.ClassName(function.Id, 3)}.{method}", BrowserCommand.Filter([function], 3), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void WithOneShard_TheClassAndTheFilterAreTheOldOnes()
    {
        Assert.Equal("Journeys", BrowserGenerator.ClassName("anything", 1));
        Assert.Equal("FullyQualifiedName~Journeys.read_legal", BrowserCommand.Filter([Function("read-legal", "Anyone", [], ["/terms"])]));
    }
}
