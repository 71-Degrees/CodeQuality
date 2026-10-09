/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Refactor;
using CodeQuality.Refactor.Fixers;

namespace CodeQuality.Refactor.Tests;

public class FileEditorTests
{
    private static readonly IReadOnlyList<IFixer> Fixers = new IFixer[] { new CultureFixer(), new StringComparisonFixer(), new SuppressFinalizeFixer() };

    private static Warning At(string text, string rule, string find, string message = "") =>
        new(rule, "f.cs", Line(text, text.IndexOf(find, StringComparison.Ordinal)), Column(text, text.IndexOf(find, StringComparison.Ordinal)), message);

    private static int Line(string text, int offset) => text[..offset].Count(c => c == '\n') + 1;

    private static int Column(string text, int offset) => offset - (text[..offset].LastIndexOf('\n') + 1) + 1;

    [Fact]
    public void Two_fixes_in_one_file_do_not_disturb_each_other_and_the_using_is_added_in_order()
    {
        var text = "using System.Text;\n\nclass A\n{\n    void M(string s)\n    {\n        var a = int.Parse(s);\n        var b = int.Parse(s);\n    }\n}\n";
        var warnings = new[]
        {
            At(text, "CA1305", "int.Parse(s);\n        var b", "'int.Parse(string)'"),
            At(text, "CA1305", "int.Parse(s);\n    }", "'int.Parse(string)'"),
        };

        var edit = FileEditor.Edit(text, warnings, Fixers);

        Assert.Equal(2, edit.Fixed.Count);
        Assert.Empty(edit.Left);
        Assert.Equal("using System.Globalization;\nusing System.Text;\n\nclass A\n{\n    void M(string s)\n    {\n        var a = int.Parse(s, CultureInfo.InvariantCulture);\n        var b = int.Parse(s, CultureInfo.InvariantCulture);\n    }\n}\n", edit.Text);
    }

    [Fact]
    public void A_using_that_is_already_there_is_not_added_again()
    {
        var text = "using System.Globalization;\n\nclass A\n{\n    int M(string s) => int.Parse(s);\n}\n";

        var edit = FileEditor.Edit(text, new[] { At(text, "CA1305", "int.Parse", "'int.Parse(string)'") }, Fixers);

        Assert.Equal(1, edit.Text.Split("using System.Globalization;").Length - 1);
    }

    [Fact]
    public void A_file_with_no_usings_gets_one_above_its_namespace()
    {
        var text = "/* header */\n\nnamespace N;\n\nclass A\n{\n    int M(string s) => int.Parse(s);\n}\n";

        var edit = FileEditor.Edit(text, new[] { At(text, "CA1305", "int.Parse", "'int.Parse(string)'") }, Fixers);

        Assert.StartsWith("/* header */\n\nusing System.Globalization;\n\nnamespace N;", edit.Text);
    }

    [Fact]
    public void A_warning_no_fixer_can_fix_is_reported_as_left_and_the_text_is_untouched()
    {
        var text = "class A { void M() { Foo(); } }";

        var edit = FileEditor.Edit(text, new[] { At(text, "CA9999", "Foo") }, Fixers);

        Assert.Single(edit.Left);
        Assert.Equal(text, edit.Text);
    }
}
