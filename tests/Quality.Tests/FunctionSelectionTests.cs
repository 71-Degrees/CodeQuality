/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;

namespace CodeQuality.Quality.Tests;

public sealed class FunctionSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quality-funcsel-" + Guid.NewGuid().ToString("N"));

    private static readonly FeatureRegistryFile Registry = FeatureRegistryFile.Parse("""
        { "functions": [
          { "id": "sign-in", "name": "Sign in", "routes": [ "/account/login" ], "tests": [ "SignInTests" ] },
          { "id": "wave", "name": "Your wave", "routes": [ "/wave" ], "tests": [ "WaveTests" ] },
          { "id": "strands", "name": "Strands", "routes": [ "/wave/strands" ], "tests": [ "StrandTests" ] },
          { "id": "chronicle", "name": "Chronicles", "routes": [ "/chronicle/{Slug}", "/chronicle/{Slug}/map" ], "tests": [ "ChronicleTests" ] },
          { "id": "home", "name": "Home", "routes": [ "/" ], "tests": [] } ] }
        """)!;

    public FunctionSelectionTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private TestPlan Plan(params string[] changed)
    {
        // Every flow test signs in through the login page and hosts the app, so a plain route-mention would select all of them.
        Write("tests/Web.Tests/Web.Tests.csproj", "<Project />");
        Write("tests/Web.Tests/SignInTests.cs", "public class SignInTests\n{\n    [Fact]\n    public void A() { Get(\"/account/login\"); }\n}\n");
        Write("tests/Web.Tests/WaveTests.cs", "public class WaveTests : IClassFixture<Factory<Program>>\n{\n    [Fact]\n    public void A() { Get(\"/account/login\"); Get(\"/wave\"); }\n}\n");
        Write("tests/Web.Tests/StrandTests.cs", "public class StrandTests : IClassFixture<Factory<Program>>\n{\n    [Fact]\n    public void A() { Get(\"/account/login\"); Get(\"/wave/strands\"); }\n}\n");
        Write("tests/Web.Tests/ChronicleTests.cs", "public class ChronicleTests\n{\n    [Fact]\n    public void A() { Get(\"/chronicle/genesis\"); }\n}\n");
        Write("src/Web/Pages/Login.razor", "@page \"/account/login\"\n<h1>Sign in</h1>\n");
        Write("src/Web/Pages/Wave.razor", "@page \"/wave\"\n<h1>Wave</h1>\n");
        Write("src/Web/Pages/Strands.razor", "@page \"/wave/strands\"\n<h1>Strands</h1>\n");
        Write("src/Web/Pages/Home.razor", "@page \"/\"\n<h1>Home</h1>\n");
        Write("src/Web/Endpoints.cs", "public static class StrandEndpoints\n{\n    public static void Map() { Post(\"/wave/strands/update\"); }\n}\n");
        Write("src/Web/Program.cs", "public class Program { static void Main() { } }\n");
        var files = changed.Select(c => new ChangedSource(c, IsTest: false)).ToList();
        return TestSelection.Plan(_root, files, [Path.Combine(_root, "tests/Web.Tests/Web.Tests.csproj")], ["src"], Registry);
    }

    private static string[] Selected(TestPlan plan) => plan.ClassesByProject.Values.SelectMany(c => c).Order().ToArray();

    [Fact]
    public void ChangingAPage_RunsTheTestsOfTheFunctionThatOwnsIt_NotEveryTestThatSignsInThroughOtherPages()
    {
        Assert.Equal(["StrandTests"], Selected(Plan("src/Web/Pages/Strands.razor")));
        Assert.Equal(["WaveTests"], Selected(Plan("src/Web/Pages/Wave.razor")));
    }

    [Fact]
    public void ChangingTheSignInPage_RunsTheSignInTests_AndNothingElseThatMerelyPassesThrough()
    {
        Assert.Equal(["SignInTests"], Selected(Plan("src/Web/Pages/Login.razor")));
    }

    [Fact]
    public void AnEndpointFile_StandsForTheAddressesItNames_PreferringTheMostSpecificFunction()
    {
        var plan = Plan("src/Web/Endpoints.cs");

        Assert.Equal(["StrandTests"], Selected(plan));
        Assert.Equal(["strands"], plan.Functions.Select(f => f.Id));
    }

    [Fact]
    public void TheCompositionRoot_IsNotSelectedForByNameBecauseEveryHostedTestMentionsIt()
    {
        var plan = Plan("src/Web/Program.cs");

        Assert.Empty(Selected(plan));
        Assert.Contains("src/Web/Program.cs", plan.NotMentioned);
    }

    [Fact]
    public void TheHomePage_IsItsOwnFunction_AndSaysSoWhenNoTestIsNamedForIt()
    {
        var plan = Plan("src/Web/Pages/Home.razor");

        Assert.Equal(["home"], plan.Functions.Select(f => f.Id));
        Assert.Empty(Selected(plan));
        Assert.Contains("src/Web/Pages/Home.razor", plan.NotMentioned);
    }

    [Fact]
    public void AFunctionsAddressWithAParameter_OwnsEverythingUnderItsFixedStart()
    {
        var owners = FunctionSelection.Owning(Registry, ["/chronicle/genesis/map/extra", "/chronicles"]);

        Assert.Equal(["chronicle"], owners.Select(f => f.Id));
    }
}
