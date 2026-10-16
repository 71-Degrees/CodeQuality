/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;

namespace CodeQuality.Quality.Tests;

public sealed class QuickCheckTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "quality-quick-" + Guid.NewGuid().ToString("N"));

    public QuickCheckTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void OnlySourceAndTestCode_NotSkippedPlaces_CountAsChanged()
    {
        var config = new ToolConfig { SkipFolders = ["/tools/CodeQuality/"] };
        string[] files =
        [
            "src/Web/Foo.cs", "src/Web/Page.razor", "tests/App.Tests/FooTests.cs", "README.md", "src/Web/wwwroot/app.css",
            "src/Web/obj/Debug/x.cs", "src/Web/wwwroot/lib/leaflet.cs", "tools/CodeQuality/src/Quality/Program.cs", "src/Web/Generated.g.cs", "docs/notes.cs",
        ];

        var changed = ChangedFiles.Sources(files, config);

        Assert.Equal(["src/Web/Foo.cs", "src/Web/Page.razor", "tests/App.Tests/FooTests.cs"], changed.Select(c => c.Relative));
        Assert.Equal([false, false, true], changed.Select(c => c.IsTest));
    }

    [Fact]
    public void WordsInCommentsAndStrings_AreNotDeclarations()
    {
        const string source = """
            /// <summary>This is a class that does it without a fuss.</summary>
            public sealed class RealTests
            {
                // a record is kept for each game, and an enum is not needed
                private const string Note = "a class like this one";
                [Fact] public void A() { }
            }
            """;

        Assert.Equal(["RealTests"], TestSelection.TestClasses(source));
        Assert.Equal(["RealTests"], TestSelection.DeclaredNames("src/Core/Real.cs", source));
    }

    [Fact]
    public void OfARazorFile_OnlyTheCodeBlockDeclaresAnything()
    {
        const string razor = "<p>a class of its own with a record of every game</p>\n@* a struct in a comment *@\n@code { private sealed record Entry(int Id); }";

        Assert.Equal(["Entry", "Page"], TestSelection.DeclaredNames("src/Web/Pages/Page.razor", razor).Order());
    }

    [Fact]
    public void DeclaredNames_AreTheTypesAFileDefines_AndARazorPageIsItsFileName()
    {
        var names = TestSelection.DeclaredNames("src/Web/Pages/Passage.razor", "@page \"/passage\"\n@code { private sealed record Entry(int Id); }");
        Assert.Equal(["Entry", "Passage"], names);

        var cs = TestSelection.DeclaredNames("src/Core/Widget.cs", "public sealed class WaveCaptureService { } public interface IWaveStore { } public enum Id { }");
        Assert.Equal(["WaveCaptureService", "IWaveStore"], cs); // "Id" is too short to mean anything in a test
    }

    [Fact]
    public void ATestFile_IsOnlyOneWhenItHoldsATest()
    {
        Assert.Equal(["FooTests"], TestSelection.TestClasses("public class FooTests { [Fact] public void A() { } }"));
        Assert.Equal(["Cases"], TestSelection.TestClasses("public class Cases { [BrowserFact] public void A() { } }"));
        Assert.Empty(TestSelection.TestClasses("public class Helpers { public static void Make() { } }"));
    }

    [Fact]
    public void TheTestsThatMentionAChangedType_AreChosen_AndTheChangedFilesNoTestMentionAreSaid()
    {
        Write("src/Core/Widget.cs", "public class Widget { }");
        Write("src/Core/Lonely.cs", "public class Lonely { }");
        var project = Write("tests/App.Tests/App.Tests.csproj", "<Project />");
        Write("tests/App.Tests/WidgetTests.cs", "public class WidgetTests { [Fact] public void A() { new Widget(); } }");
        Write("tests/App.Tests/OtherTests.cs", "public class OtherTests { [Fact] public void B() { } }");

        var plan = TestSelection.Plan(_root, [new ChangedSource("src/Core/Widget.cs", false), new ChangedSource("src/Core/Lonely.cs", false)], [project]);

        Assert.Equal(["WidgetTests"], plan.ClassesByProject[project]);
        Assert.Equal(["src/Core/Lonely.cs"], plan.NotMentioned);
    }

    [Fact]
    public void AChangedTestFile_RunsItsOwnClasses_AndAProjectWithNothingSelectedIsLeftOut()
    {
        var first = Write("tests/One.Tests/One.Tests.csproj", "<Project />");
        var second = Write("tests/Two.Tests/Two.Tests.csproj", "<Project />");
        Write("tests/One.Tests/OneTests.cs", "public class OneTests { [Fact] public void A() { } }");
        Write("tests/Two.Tests/TwoTests.cs", "public class TwoTests { [Fact] public void B() { } }");

        var plan = TestSelection.Plan(_root, [new ChangedSource("tests/One.Tests/OneTests.cs", true)], [first, second]);

        Assert.Equal(["OneTests"], plan.ClassesByProject[first]);
        Assert.False(plan.ClassesByProject.ContainsKey(second));
    }

    [Fact]
    public void AChangedComponent_IsFoundThroughThePagesThatShowIt_AndTheTestsThatRequestThem()
    {
        Write("src/Web/Shared/Feed.razor", "<p>feed</p>");
        Write("src/Web/Pages/Wave.razor", "@page \"/wave\"\n<Feed Items=\"x\" />");
        Write("src/Web/Pages/Home.razor", "@page \"/\"\n<p>home</p>");
        var project = Write("tests/App.Tests/App.Tests.csproj", "<Project />");
        Write("tests/App.Tests/PageTests.cs", "public class PageTests { [Fact] public async Task A() { await Get(\"/wave?order=saved\"); } }");
        Write("tests/App.Tests/HomeTests.cs", "public class HomeTests { [Fact] public async Task B() { await Get(\"/\"); } }");

        Assert.Equal(["/wave"], TestSelection.RoutesShowing(_root, ["src"], "src/Web/Shared/Feed.razor"));
        var plan = TestSelection.Plan(_root, [new ChangedSource("src/Web/Shared/Feed.razor", false)], [project], ["src"]);

        Assert.Equal(["PageTests"], plan.ClassesByProject[project]); // not HomeTests: "/" would match nearly anything
        Assert.Empty(plan.NotMentioned);
    }

    [Fact]
    public void TheFilter_RunsExactlyTheseClasses()
    {
        Assert.Equal("FullyQualifiedName~.FooTests.|FullyQualifiedName~+FooTests.|FullyQualifiedName~.BarTests.|FullyQualifiedName~+BarTests.", TestSelection.Filter(["FooTests", "BarTests"]));
    }
}
