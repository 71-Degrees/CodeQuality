/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;
using CodeQuality.Quality;

namespace CodeQuality.Quality.Tests;

public class FeatureFunctionsTests
{
    private const string BillingSource = """
        public class BillingTests
        {
            private sealed class FakeGateway
            {
                public void NotATest() { }
            }

            [Fact]
            public async Task Checkout_Works() { }

            [Theory]
            [InlineData("a]b")]
            public void Webhook_Works(string x) { }
        }

        public sealed class OtherTests
        {
            [Fact]
            public void One() { }
        }
        """;

    private static readonly List<TestClassInfo> Index = FeatureFunctions.IndexTests(
    [
        ("tests/Web.Tests", "tests/Web.Tests/BillingTests.cs", BillingSource),
        ("tests/Browser.Tests", "tests/Browser.Tests/FlowBrowserTests.cs", "public class FlowBrowserTests\n{\n    [BrowserFact]\n    public async Task Flow() { }\n}"),
    ]);

    private static PageRow Page(string route, bool crawled = false, bool signedIn = false, bool parameters = false, int? clicks = 1) =>
        new(route, "src/P.razor", "x", clicks, "/", parameters, signedIn, crawled, []);

    private static FeatureFunction Fn(string id, string[]? routes = null, string[]? tests = null, string[]? steps = null) =>
        new() { Id = id, Name = id, Area = "A", Who = "Anyone", Routes = routes ?? [], Tests = tests ?? [], Steps = steps ?? [] };

    [Fact]
    public void ATestBelongsToItsTopLevelClass_NotToAFakeDeclaredAboveIt()
    {
        var billing = Index.Single(c => c.Name == "BillingTests");

        Assert.Equal(["Checkout_Works", "Webhook_Works"], billing.Tests);
        Assert.Equal(["One"], Index.Single(c => c.Name == "OtherTests").Tests);
        Assert.DoesNotContain(Index, c => c.Name == "FakeGateway");
    }

    [Fact]
    public void AClassIsBrowserUnitOrComponentByWhereItLives() =>
        Assert.Equal(["unit/web", "unit/web", "browser"], Index.Select(c => c.Layer));

    [Fact]
    public void ANamedClassCountsAllItsTests_AndAClassDotMethodCountsOne()
    {
        var registry = new FeatureRegistryFile { Functions = [Fn("a", tests: ["BillingTests"]), Fn("b", tests: ["BillingTests.Checkout_Works"])] };

        var result = FeatureFunctions.Resolve(registry, [], Index);

        Assert.Equal(2, result.Functions[0].TestCount);
        Assert.Equal(1, result.Functions[1].TestCount);
    }

    [Fact]
    public void ATestNamedButNotFoundIsReported_AndAFunctionWithNoneIsNotCovered()
    {
        var registry = new FeatureRegistryFile { Functions = [Fn("a", tests: ["NoSuchTests", "BillingTests.NoSuchMethod"]), Fn("b")] };

        var result = FeatureFunctions.Resolve(registry, [], Index);

        Assert.Equal(["NoSuchTests", "BillingTests.NoSuchMethod"], result.Functions[0].Missing);
        Assert.Equal("none", result.Functions[0].Status);
        Assert.Equal("none", result.Functions[1].Status);
    }

    [Fact]
    public void AFunctionIsCoveredByABrowserTestOnlyWhenOneOfItsTestsIsABrowserTest()
    {
        var registry = new FeatureRegistryFile { Functions = [Fn("a", tests: ["BillingTests"]), Fn("b", tests: ["BillingTests", "FlowBrowserTests"])] };

        var result = FeatureFunctions.Resolve(registry, [], Index);

        Assert.Equal(["weak", "browser"], result.Functions.Select(f => f.Status));
    }

    [Fact]
    public void ClicksAreTheStepsThatStartWithClick_TheRestAreEntries()
    {
        var registry = new FeatureRegistryFile { Functions = [Fn("a", steps: ["Click Play", "Type the code", "Click Join", "Scan the QR code"])] };

        var row = FeatureFunctions.Resolve(registry, [], Index).Functions[0];

        Assert.Equal((2, 2), (row.Clicks, row.Entries));
    }

    [Fact]
    public void APageThatBelongsToNoFunctionIsReported_ExceptThoseListedAsNotFunctions()
    {
        var registry = new FeatureRegistryFile { NotFunctions = ["/error"], Functions = [Fn("a", routes: ["/play"])] };

        var result = FeatureFunctions.Resolve(registry, [Page("/play"), Page("/orphan"), Page("/Error")], Index);

        Assert.Equal(["/orphan"], result.UnassignedPages);
    }

    [Fact]
    public void ARouteThatIsNotAPageIsReported()
    {
        var registry = new FeatureRegistryFile { Functions = [Fn("a", routes: ["/play", "/gone"])] };

        var result = FeatureFunctions.Resolve(registry, [Page("/play")], Index);

        Assert.Equal([("a", "/gone")], result.UnknownRoutes);
    }

    [Fact]
    public void TheNavigationDepthIsTheFewestClicksToAnyOfItsPages()
    {
        var registry = new FeatureRegistryFile { Functions = [Fn("a", routes: ["/a", "/b"])] };

        var row = FeatureFunctions.Resolve(registry, [Page("/a", clicks: 3), Page("/b", clicks: 2)], Index).Functions[0];

        Assert.Equal(2, row.NavigationDepth);
    }

    [Theory]
    [InlineData("crawled")]
    [InlineData("needs sign-in")]
    [InlineData("needs an id")]
    [InlineData("not crawled")]
    [InlineData("not a page")]
    public void TheCrawlStateSaysWhyAFunctionIsOrIsNotReachedByTheCrawler(string expected)
    {
        IReadOnlyList<PageRow> pages = expected switch
        {
            "crawled" => [Page("/a", crawled: true)],
            "needs sign-in" => [Page("/a", signedIn: true)],
            "needs an id" => [Page("/a", parameters: true)],
            "not crawled" => [Page("/a")],
            _ => [],
        };

        Assert.Equal(expected, FeatureFunctions.CrawlOf(pages));
    }

    [Fact]
    public void TheListIsReadWithCommentsAndTrailingCommas()
    {
        var folder = Path.Combine(Path.GetTempPath(), "cq-features-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "quality"));
        var json = """
            { // a comment
              "notFunctions": ["/error",],
              "functions": [ { "id": "a", "name": "A", "steps": ["Click x"], } ] }
            """;
        File.WriteAllText(Path.Combine(folder, "quality", "features.json"), json);

        var registry = FeatureRegistryFile.Load(folder, null);

        Assert.Equal("a", registry!.Functions[0].Id);
        Assert.Equal(["/error"], registry.NotFunctions);
        Assert.Null(FeatureRegistryFile.Load(Path.Combine(folder, "nowhere"), null));
        Directory.Delete(folder, recursive: true);
    }
}
