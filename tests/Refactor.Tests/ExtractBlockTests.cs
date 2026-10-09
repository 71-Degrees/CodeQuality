/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Refactor;

namespace CodeQuality.Refactor.Tests;

public class ExtractBlockTests
{
    private const string Source =
        "/*\n * Copyright\n */\n\nusing System.Text;\nusing Foo.Bar;\n\nvar builder = Create();\nvar app = builder.Build();\napp.MapGet(\"/a\", () => 1);\napp.MapGet(\"/b\", () => 2);\napp.Run();\n";

    private static ExtractRequest Request(int from, int to, string parameters = "", string? call = null) =>
        new(from, to, "OpsEndpoints", "MapOps", "WebApplication app", parameters, "Sample.Web", "The ops routes.", call);

    // ---- Indenter ----

    [Fact]
    public void A_block_is_indented_and_blank_lines_stay_blank()
    {
        var indented = Indenter.Indent("a();\n\nb();", 8);

        Assert.Equal("        a();\n\n        b();", indented);
    }

    [Fact]
    public void The_lines_of_a_multi_line_verbatim_string_are_not_indented_because_the_spaces_are_part_of_the_string()
    {
        var block = "var s = @\"first\n  second\nthird\";\nDone();";

        var indented = Indenter.Indent(block, 4);

        Assert.Equal("    var s = @\"first\n  second\nthird\";\n    Done();", indented);
    }

    [Fact]
    public void A_raw_string_moves_as_one_piece_so_its_content_keeps_its_relative_indent()
    {
        var block = "var s = \"\"\"\n    text\n    \"\"\";";

        var indented = Indenter.Indent(block, 4);

        Assert.Equal("    var s = \"\"\"\n        text\n        \"\"\";", indented);
    }

    // ---- ParameterList ----

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("string a", new[] { "a" })]
    [InlineData("string? a, int b", new[] { "a", "b" })]
    [InlineData("Func<HttpContext, IResult?> authorize, bool flag", new[] { "authorize", "flag" })]
    [InlineData("int n = 3, (int, int) pair", new[] { "n", "pair" })]
    public void Parameter_names_are_read_from_the_list(string parameters, string[] expected) =>
        Assert.Equal(expected, ParameterList.Names(parameters));

    // ---- Plan ----

    [Fact]
    public void The_block_becomes_a_method_in_a_new_file_and_one_call_is_left_in_the_source()
    {
        var plan = ExtractBlock.Plan(Source, null, Request(10, 11));

        Assert.Equal("/*\n * Copyright\n */\n\nusing System.Text;\nusing Foo.Bar;\n\nvar builder = Create();\nvar app = builder.Build();\napp.MapOps();\napp.Run();\n", plan.Source);
        Assert.Equal(2, plan.MovedLines);
        Assert.StartsWith("/*\n * Copyright\n */\n\nusing System.Text;\nusing Foo.Bar;\n\nnamespace Sample.Web;\n\ninternal static class OpsEndpoints\n{\n", plan.Target);
        Assert.Contains("    /// <summary>The ops routes.</summary>\n    public static void MapOps(this WebApplication app)\n    {\n        app.MapGet(\"/a\", () => 1);\n        app.MapGet(\"/b\", () => 2);\n    }\n}\n", plan.Target);
    }

    [Fact]
    public void The_call_passes_the_parameters_the_block_needs_by_name()
    {
        var plan = ExtractBlock.Plan(Source, null, Request(10, 11, "string key, Func<int, bool> check"));

        Assert.Contains("app.MapOps(key, check);", plan.Source);
        Assert.Contains("MapOps(this WebApplication app, string key, Func<int, bool> check)", plan.Target);
    }

    [Fact]
    public void A_second_method_is_added_to_the_class_that_is_already_there()
    {
        var first = ExtractBlock.Plan(Source, null, Request(10, 10));

        var second = ExtractBlock.Plan(first.Source, first.Target, new ExtractRequest(10, 10, "OpsEndpoints", "MapMore", "WebApplication app", string.Empty, "Sample.Web", string.Empty));

        Assert.Contains("public static void MapOps(", second.Target);
        Assert.Contains("public static void MapMore(", second.Target);
        Assert.Equal(1, second.Target.Split("internal static class OpsEndpoints").Length - 1);
        Assert.EndsWith("    }\n}\n", second.Target);
    }

    [Fact]
    public void Windows_line_endings_are_kept_in_both_files()
    {
        var plan = ExtractBlock.Plan(Source.Replace("\n", "\r\n", StringComparison.Ordinal), null, Request(10, 11));

        // every line break is a CRLF: removing them all leaves no stray LF
        Assert.DoesNotContain("\n", plan.Source.Replace("\r\n", string.Empty, StringComparison.Ordinal));
        Assert.DoesNotContain("\n", plan.Target.Replace("\r\n", string.Empty, StringComparison.Ordinal));
        Assert.Contains("\r\n", plan.Target);
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(5, 4)]
    [InlineData(10, 99)]
    public void A_range_outside_the_file_is_refused(int from, int to) =>
        Assert.Throws<ArgumentException>(() => ExtractBlock.Plan(Source, null, Request(from, to)));
}
