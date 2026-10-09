/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Refactor;

namespace CodeQuality.Refactor.Tests;

public class SourceTextTests
{
    [Theory]
    [InlineData("abc\ndef\n", 1, 1, 0)]
    [InlineData("abc\ndef\n", 2, 2, 5)]
    [InlineData("a\r\nb\r\n", 2, 1, 3)]
    public void A_line_and_column_become_an_offset(string text, int line, int column, int expected) =>
        Assert.Equal(expected, SourceText.Offset(text, line, column));

    [Fact]
    public void A_line_past_the_end_is_reported_as_not_found() => Assert.Equal(-1, SourceText.Offset("abc", 5, 1));

    [Theory]
    [InlineData("The behavior of 'int.Parse(string)' could vary", "Parse")]
    [InlineData("The behavior of 'StringBuilder.Append(ref StringBuilder.AppendInterpolatedStringHandler)' could vary", "Append")]
    [InlineData("The behavior of 'string.StartsWith(string)' could vary", "StartsWith")]
    public void The_method_an_analyzer_message_talks_about_is_found(string message, string expected) =>
        Assert.Equal(expected, SourceText.MethodNameIn(message));

    [Fact]
    public void The_owned_fields_are_read_from_the_message() =>
        Assert.Equal(new[] { "_a", "_b" }, SourceText.OwnedFieldNames("Type 'X' owns disposable field(s) '_a, _b' but is not disposable"));

    [Fact]
    public void Build_output_becomes_distinct_warnings()
    {
        var line = @"C:\repo\src\A.cs(12,9): warning CA1816: Change A.Dispose() to call GC.SuppressFinalize(object). [C:\repo\src\A.csproj::TargetFramework=net10.0]";

        var warnings = BuildWarnings.Parse(line + "\n" + line.Replace("net10.0", "net9.0") + "\n");

        var warning = Assert.Single(warnings);
        Assert.Equal(("CA1816", @"C:\repo\src\A.cs", 12, 9), (warning.Rule, warning.File, warning.Line, warning.Column));
    }
}
