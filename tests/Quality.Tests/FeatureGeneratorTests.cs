/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;

namespace CodeQuality.Quality.Tests;

public sealed class FeatureGeneratorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quality-featuregen-" + Guid.NewGuid().ToString("N"));

    public FeatureGeneratorTests()
    {
        Write("src/Web/Pages/Index.razor", "@page \"/\"\n<PageTitle>Home - Site</PageTitle>\n<a href=\"about\">About us</a>\n");
        Write("src/Web/Pages/About.razor", "@page \"/about\"\n<PageTitle>About - Site</PageTitle>\n<label for=\"n\">Your name</label>\n<button type=\"submit\">Send</button>\n");
        Write("src/Web/Pages/Admin.razor", "@page \"/admin\"\n@attribute [Authorize(Roles = \"Admin\")]\n<h1>Admin desk</h1>\n<a href=\"/\">Home</a>\n");
        Write("src/Web/Pages/Error.razor", "@page \"/Error\"\n<h1>Oops</h1>\n");
        Write("tests/Web.Tests/AboutTests.cs", "public class AboutTests\n{\n    [Fact]\n    public async Task ShowsAbout() { await Get(\"/about\"); }\n}\n");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public async Task APageBecomesAFunction_NamedFromItsTitle_WithTheRealLinkTextAsItsSteps()
    {
        var drafted = await FeatureGenerator.DraftAsync(_root, new ToolConfig());

        var about = drafted.Functions.Single(f => f.Routes.Contains("/about"));
        Assert.Equal("About", about.Name);
        Assert.Equal("Anyone", about.Who);
        Assert.Equal(["Click About us", "Type Your name", "Click Send"], about.Steps);
        Assert.Contains("AboutTests", about.Tests);
        Assert.True(about.Generated);
    }

    [Fact]
    public async Task AnAdminPageSaysWhoMayUseIt_AndTheErrorPageIsSetAside()
    {
        var drafted = await FeatureGenerator.DraftAsync(_root, new ToolConfig());

        Assert.Equal("Admin", drafted.Functions.Single(f => f.Routes.Contains("/admin")).Who);
        Assert.Contains("/Error", drafted.NotFunctions);
        Assert.DoesNotContain(drafted.Functions, f => f.Routes.Contains("/Error"));
    }

    [Fact]
    public async Task AListThatExistsIsNeverRewritten_TheDraftsGoToAProposalFile()
    {
        Write("quality/features.json", "{ // mine\n \"notFunctions\": [], \"functions\": [ { \"id\": \"about\", \"name\": \"About\", \"routes\": [ \"/about\" ] } ] }");
        var before = File.ReadAllText(Path.Combine(_root, "quality/features.json"));

        var (path, proposed, drafted) = await FeatureGenerator.WriteAsync(_root, new ToolConfig());

        Assert.True(proposed);
        Assert.Equal(before, File.ReadAllText(Path.Combine(_root, "quality/features.json")));
        Assert.EndsWith("features-proposed.json", path, StringComparison.Ordinal);
        Assert.DoesNotContain(drafted.Functions, f => f.Routes.Contains("/about"));
        Assert.True(drafted.AlreadyCovered >= 1);
    }

    [Fact]
    public async Task TheHomePageNamesNoTests_AndTestsNamedForThePageComeFirst()
    {
        Write("tests/Web.Tests/AAAHelperTests.cs", "public class AAAHelperTests\n{\n    [Fact]\n    public async Task Visits() { await Get(\"/about\"); }\n}\n");

        var drafted = await FeatureGenerator.DraftAsync(_root, new ToolConfig());

        Assert.Empty(drafted.Functions.Single(f => f.Routes.Contains("/")).Tests);
        Assert.Equal("AboutTests", drafted.Functions.Single(f => f.Routes.Contains("/about")).Tests[0]);
    }

    [Fact]
    public async Task OnlyTheSubmitButtonIsAStep_AndADeniedPageIsSetAside()
    {
        Write("src/Web/Pages/Dialog.razor", "@page \"/dialog\"\n<h1>Dialog</h1>\n<button type=\"button\">Close</button>\n");
        Write("src/Web/Pages/Denied.razor", "@page \"/account/denied\"\n<h1>Not allowed</h1>\n");

        var drafted = await FeatureGenerator.DraftAsync(_root, new ToolConfig());

        Assert.DoesNotContain("Click Close", drafted.Functions.Single(f => f.Routes.Contains("/dialog")).Steps);
        Assert.Contains("/account/denied", drafted.NotFunctions);
    }
}
