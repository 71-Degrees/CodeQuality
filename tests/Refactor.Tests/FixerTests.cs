/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Refactor;
using CodeQuality.Refactor.Fixers;

namespace CodeQuality.Refactor.Tests;

/// <summary>Each fixer on the exact shape of code it is meant for, and on the shapes it must leave alone.</summary>
public class FixerTests
{
    private static Warning Warn(string rule, string message = "") => new(rule, "f.cs", 1, 1, message);

    private static string? Fix(IFixer fixer, string text, string at, string rule, string message = "") =>
        fixer.Fix(text, text.IndexOf(at, StringComparison.Ordinal), Warn(rule, message));

    // ---- CA1816 ----

    [Fact]
    public void Dispose_gets_a_SuppressFinalize_call_as_its_last_statement()
    {
        var text = "class A : IDisposable\n{\n    public void Dispose()\n    {\n        _x.Dispose();\n    }\n}\n";

        var fixedText = Fix(new SuppressFinalizeFixer(), text, "Dispose()", "CA1816");

        Assert.Equal("class A : IDisposable\n{\n    public void Dispose()\n    {\n        _x.Dispose();\n        GC.SuppressFinalize(this);\n    }\n}\n", fixedText);
    }

    [Fact]
    public void An_expression_bodied_Dispose_becomes_a_block_with_the_call_added()
    {
        var text = "class A\n{\n    public void Dispose() => _lock.Dispose();\n}\n";

        var fixedText = Fix(new SuppressFinalizeFixer(), text, "Dispose()", "CA1816");

        Assert.Equal("class A\n{\n    public void Dispose()\n    {\n        _lock.Dispose();\n        GC.SuppressFinalize(this);\n    }\n}\n", fixedText);
    }

    [Fact]
    public void A_Dispose_that_already_suppresses_finalization_is_left_alone()
    {
        var text = "class A\n{\n    public void Dispose()\n    {\n        GC.SuppressFinalize(this);\n    }\n}\n";

        Assert.Null(Fix(new SuppressFinalizeFixer(), text, "Dispose()", "CA1816"));
    }

    [Fact]
    public void Windows_line_endings_are_kept()
    {
        var text = "class A\r\n{\r\n    public void Dispose()\r\n    {\r\n        _x.Dispose();\r\n    }\r\n}\r\n";

        var fixedText = Fix(new SuppressFinalizeFixer(), text, "Dispose()", "CA1816");

        Assert.Contains("        GC.SuppressFinalize(this);\r\n    }", fixedText);
        Assert.DoesNotContain("\n\n", fixedText!.Replace("\r\n", "\n\r"));
    }

    // ---- CA1852 ----

    [Theory]
    [InlineData("internal class Foo { }", "internal sealed class Foo { }")]
    [InlineData("internal partial class Foo { }", "internal sealed partial class Foo { }")]
    public void A_class_nothing_inherits_from_is_sealed(string text, string expected) =>
        Assert.Equal(expected, Fix(new SealClassFixer(), text, "Foo", "CA1852"));

    [Theory]
    [InlineData("internal abstract class Foo { }")]
    [InlineData("internal static class Foo { }")]
    [InlineData("internal sealed class Foo { }")]
    public void Abstract_static_and_already_sealed_classes_are_left_alone(string text) =>
        Assert.Null(Fix(new SealClassFixer(), text, "Foo", "CA1852"));

    // ---- CA1305 ----

    [Theory]
    [InlineData("var n = int.Parse(s);", "int.Parse(s)", "'int.Parse(string)'", "var n = int.Parse(s, CultureInfo.InvariantCulture);")]
    [InlineData("var s = n.ToString(\"D6\");", "n.ToString", "'int.ToString(string)'", "var s = n.ToString(\"D6\", CultureInfo.InvariantCulture);")]
    [InlineData("var s = n.ToString();", "n.ToString", "'int.ToString()'", "var s = n.ToString(CultureInfo.InvariantCulture);")]
    [InlineData("var s = string.Format(\"{0}\", n);", "string.Format", "'string.Format(string, object)'", "var s = string.Format(CultureInfo.InvariantCulture, \"{0}\", n);")]
    [InlineData("var ok = int.TryParse(s, out var n);", "int.TryParse", "'int.TryParse(string, out int)'", "var ok = int.TryParse(s, CultureInfo.InvariantCulture, out var n);")]
    [InlineData("var n = Convert.ToInt32(s);", "Convert.ToInt32", "'Convert.ToInt32(string)'", "var n = Convert.ToInt32(s, CultureInfo.InvariantCulture);")]
    public void A_culture_dependent_call_names_the_invariant_culture(string text, string at, string message, string expected) =>
        Assert.Equal(expected, Fix(new CultureFixer(), text, at, "CA1305", message));

    [Fact]
    public void A_conditional_access_call_is_fixed_where_the_compiler_points_at_the_whole_expression()
    {
        var text = "var when = n.Event.EventTime?.ToString(\"dddd, MMM d\") ?? \"\";";

        var fixedText = Fix(new CultureFixer(), text, "n.Event", "CA1305", "'DateTimeOffset.ToString(string)'");

        Assert.Equal("var when = n.Event.EventTime?.ToString(\"dddd, MMM d\", CultureInfo.InvariantCulture) ?? \"\";", fixedText);
    }

    [Fact]
    public void RenderMessage_is_given_the_invariant_culture()
    {
        var fixedText = Fix(new CultureFixer(), "var m = e.RenderMessage();", "e.RenderMessage", "CA1305", "'LogEvent.RenderMessage([IFormatProvider])'");

        Assert.Equal("var m = e.RenderMessage(CultureInfo.InvariantCulture);", fixedText);
    }

    [Fact]
    public void An_interpolated_Append_takes_the_culture_first()
    {
        var text = "sb.Append($\"<p>{n}</p>\");";

        // the compiler points at the dot before Append
        var fixedText = Fix(new CultureFixer(), text, ".Append", "CA1305", "'StringBuilder.Append(ref StringBuilder.AppendInterpolatedStringHandler)'");

        Assert.Equal("sb.Append(CultureInfo.InvariantCulture, $\"<p>{n}</p>\");", fixedText);
    }

    [Fact]
    public void When_two_calls_start_at_the_same_place_the_one_the_message_names_is_fixed()
    {
        var text = "var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString(\"D6\");";

        var fixedText = Fix(new CultureFixer(), text, "RandomNumberGenerator", "CA1305", "'int.ToString(string)'");

        Assert.Equal("var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString(\"D6\", CultureInfo.InvariantCulture);", fixedText);
    }

    [Fact]
    public void A_shape_the_fixer_does_not_know_is_left_for_a_person()
    {
        Assert.Null(Fix(new CultureFixer(), "var s = x.Join(y);", "x.Join", "CA1305", "'Foo.Join(string)'"));
    }

    // ---- CA1310 ----

    [Theory]
    [InlineData("var ok = text.StartsWith(\"//\");", "text.StartsWith", "'string.StartsWith(string)'", "var ok = text.StartsWith(\"//\", StringComparison.Ordinal);")]
    [InlineData("var i = text.IndexOf(\"x\", 3);", "text.IndexOf", "'string.IndexOf(string, int)'", "var i = text.IndexOf(\"x\", 3, StringComparison.Ordinal);")]
    public void A_culture_sensitive_string_comparison_becomes_ordinal(string text, string at, string message, string expected) =>
        Assert.Equal(expected, Fix(new StringComparisonFixer(), text, at, "CA1310", message));

    // ---- CA1001 ----

    [Fact]
    public void A_class_that_owns_a_lock_becomes_disposable_and_disposes_it()
    {
        var text = "public class Store\n{\n    private readonly SemaphoreSlim _lock = new(1, 1);\n\n    public void Run()\n    {\n    }\n}\n";

        var fixedText = Fix(new DisposableOwnerFixer(), text, "Store", "CA1001", "Type 'Store' owns disposable field(s) '_lock' but is not disposable");

        Assert.Contains("public class Store : IDisposable", fixedText);
        Assert.Contains("    public void Dispose()\n    {\n        _lock.Dispose();\n        GC.SuppressFinalize(this);\n    }\n}", fixedText);
    }

    [Fact]
    public void An_existing_base_list_gets_IDisposable_added_and_a_nullable_field_is_disposed_safely()
    {
        var text = "public class Store : IStore\n{\n    private SemaphoreSlim? _a;\n    private readonly SemaphoreSlim _b = new(1);\n}\n";

        var fixedText = Fix(new DisposableOwnerFixer(), text, "Store", "CA1001", "Type 'Store' owns disposable field(s) '_a, _b' but is not disposable");

        Assert.Contains("public class Store : IStore, IDisposable", fixedText);
        Assert.Contains("_a?.Dispose();", fixedText);
        Assert.Contains("_b.Dispose();", fixedText);
    }

    [Fact]
    public void A_class_that_already_has_a_Dispose_method_is_left_for_a_person()
    {
        var text = "public class Store\n{\n    private SemaphoreSlim _lock = new(1);\n    public void Dispose() { }\n}\n";

        Assert.Null(Fix(new DisposableOwnerFixer(), text, "Store", "CA1001", "Type 'Store' owns disposable field(s) '_lock' but is not disposable"));
    }
}
