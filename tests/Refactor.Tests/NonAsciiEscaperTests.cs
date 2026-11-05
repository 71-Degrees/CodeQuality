/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text;
using CodeQuality.Refactor;

namespace CodeQuality.Refactor.Tests;

public sealed class NonAsciiEscaperTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "cq-escape-" + Guid.NewGuid().ToString("N"));

    public NonAsciiEscaperTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void An_ellipsis_inside_a_string_becomes_a_u_escape()
    {
        var result = NonAsciiEscaper.Apply("class A { string S = \"a\u2026b\"; }");

        Assert.Equal("class A { string S = \"a\\u2026b\"; }", result.Text);
        Assert.Equal(1, result.Escaped);
    }

    [Fact]
    public void An_emoji_becomes_a_capital_U_escape_for_its_surrogate_pair()
    {
        var result = NonAsciiEscaper.Apply("class A { string S = \"\uD83D\uDE00\"; }");

        Assert.Equal("class A { string S = \"\\U0001F600\"; }", result.Text);
    }

    [Fact]
    public void A_char_literal_is_escaped()
    {
        Assert.Equal("class A { char C = '\\u00E9'; }", NonAsciiEscaper.Apply("class A { char C = '\u00E9'; }").Text);
    }

    [Fact]
    public void A_verbatim_string_is_left_alone_and_reported()
    {
        var source = "class A { string V = @\"\u2026\"; }";

        var result = NonAsciiEscaper.Apply(source);

        Assert.Equal(source, result.Text);
        Assert.Equal(0, result.Escaped);
        Assert.Equal(new[] { "1: verbatim string" }, result.Left);
    }

    [Fact]
    public void Only_the_text_part_of_an_interpolated_string_changes()
    {
        var result = NonAsciiEscaper.Apply("class A { string I(int y) => $\"x \u2013 {y}\"; }");

        Assert.Equal("class A { string I(int y) => $\"x \\u2013 {y}\"; }", result.Text);
    }

    [Fact]
    public void A_file_with_a_byte_order_mark_keeps_it()
    {
        var path = WriteFile("Bom.cs", "class A { string S = \"\u2026\"; }", withBom: true);

        var result = NonAsciiEscaper.ApplyToFile(path, write: true);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        Assert.Equal("class A { string S = \"\\u2026\"; }", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
        Assert.Equal(1, result.Escaped);
    }

    [Fact]
    public void A_file_with_crlf_line_endings_keeps_them()
    {
        var source = "class A\r\n{\r\n    string S = \"\u2026\";\r\n}\r\n";

        var result = NonAsciiEscaper.Apply(source);

        Assert.Equal("class A\r\n{\r\n    string S = \"\\u2026\";\r\n}\r\n", result.Text);
    }

    [Fact]
    public void A_dry_run_reports_the_count_and_writes_nothing()
    {
        var source = "class A { string S = \"\u2026\"; }";
        var path = WriteFile("Dry.cs", source, withBom: false);

        var result = NonAsciiEscaper.ApplyToFile(path, write: false);

        Assert.Equal(1, result.Escaped);
        Assert.Equal(source, File.ReadAllText(path));
    }

    private string WriteFile(string name, string text, bool withBom)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: withBom));
        return path;
    }
}
