/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using CodeQuality.Common;
using CodeQuality.Quality;

namespace CodeQuality.Quality.Tests;

public sealed class RuleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cq-rules-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private IReadOnlyList<Finding> Scan(string relative, string source, ToolConfig? config = null, bool includeTests = false)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
        return Smells.Scan(_root, includeTests, config ?? new ToolConfig());
    }

    [Fact]
    public void StaticStateThatCanChangeIsFlaggedButConstantsAndReadonlyValuesAreNot()
    {
        var findings = Scan("src/A.cs", """
            using System.Collections.Concurrent;
            class A
            {
                static int counter;
                static readonly string Name = "x";
                const int Limit = 5;
                static readonly ConcurrentDictionary<string, int> Cache = new();
                static string Current { get; set; } = "";
                [System.ThreadStatic] static int perThread;
            }
            """);

        var names = findings.Where(f => f.Rule == "MutableStatic").Select(f => f.Member).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.Equal(new[] { "A.Cache", "A.Current", "A.counter" }, names);
    }

    [Fact]
    public void ClockHttpClientAndDroppedTasksAreCountedOncePerFile()
    {
        var findings = Scan("src/B.cs", """
            using System;
            using System.Net.Http;
            using System.Threading.Tasks;
            class B
            {
                void M()
                {
                    var a = DateTime.UtcNow;
                    var b = DateTimeOffset.Now;
                    var c = new HttpClient();
                    _ = DoAsync();
                    _ = Task.Run(() => { });
                    _ = Compute();
                }
                Task DoAsync() => Task.CompletedTask;
                int Compute() => 1;
            }
            """);

        Assert.Equal(2, findings.Single(f => f.Rule == "DirectClock").Value);
        Assert.Equal(1, findings.Single(f => f.Rule == "DirectHttpClient").Value);
        Assert.Equal(2, findings.Single(f => f.Rule == "DroppedTask").Value);
    }

    [Fact]
    public void ATestWithNoAssertionIsFlaggedEvenWhenTestsAreNotOtherwiseScanned()
    {
        var findings = Scan("tests/CTests.cs", """
            using Xunit;
            public class CTests
            {
                [Fact] public void Proves() { Assert.True(true); }
                [Fact] public void Throws() { Assert.Throws<System.InvalidOperationException>(() => { }); }
                [Fact] public void Proves_nothing() { var x = 1; }
                [Fact(Skip = "later")] public void Skipped() { Assert.True(true); }
            }
            """);

        Assert.Equal("CTests.Proves_nothing", Assert.Single(findings, f => f.Rule == "TestWithoutAssert").Member);
        Assert.Equal("CTests.Skipped", Assert.Single(findings, f => f.Rule == "SkippedTest").Member);
        Assert.DoesNotContain(findings, f => f.Rule == "MutableStatic");
    }

    [Fact]
    public void ARuleCanBeSwitchedOffOrExemptedWhereItIsTheEdge()
    {
        const string source = "using System;\nclass D { void M() { var t = DateTime.UtcNow; } }\n";
        Assert.Contains(Scan("src/Startup/D.cs", source), f => f.Rule == "DirectClock");

        Assert.DoesNotContain(Scan("src/Startup/D.cs", source, new ToolConfig { DisabledRules = new[] { "DirectClock" } }), f => f.Rule == "DirectClock");
        Assert.DoesNotContain(Scan("src/Startup/D.cs", source, new ToolConfig { RuleExemptions = new() { ["DirectClock"] = new[] { "/Startup/" } } }), f => f.Rule == "DirectClock");
    }

    private const string Block = """
        void NAME(int a, string b)
        {
            var total = 0;
            foreach (var ch in b)
            {
                if (ch == 'x') { total += 1; }
                else if (ch == 'y') { total += 2; }
                else { total -= a; }
            }
            if (total > 10) { System.Console.WriteLine(total); }
            System.Console.WriteLine(b.Length + total);
        }
        """;

    [Fact]
    public void TheSameCodeUnderOtherNamesInTwoFilesIsADuplicate()
    {
        Scan("src/One.cs", "class One\n{\n" + Block.Replace("NAME", "Alpha", StringComparison.Ordinal) + "\n}\n");
        var findings = Scan("src/Two.cs", "class Two\n{\n" + Block.Replace("NAME", "Beta", StringComparison.Ordinal) + "\n}\n");

        var duplicates = findings.Where(f => f.Rule == "DuplicateCode").ToList();

        Assert.Equal(2, duplicates.Count);
        Assert.Contains(duplicates, f => f.File == "src/One.cs" && f.Message.Contains("src/Two.cs", StringComparison.Ordinal));
        Assert.All(duplicates, f => Assert.True(f.Value >= 8));
    }

    [Fact]
    public void DifferentCodeIsNotADuplicate()
    {
        Scan("src/One.cs", "class One\n{\n" + Block.Replace("NAME", "Alpha", StringComparison.Ordinal) + "\n}\n");
        var findings = Scan("src/Two.cs", "class Two\n{\n    int F(int a) { return a + 1; }\n    int G(int a) { return a * 2; }\n}\n");

        Assert.DoesNotContain(findings, f => f.Rule == "DuplicateCode");
    }

    [Fact]
    public void TwoTablesOfLiteralConstantsAreNotADuplicate()
    {
        static string Table(string name, string word) =>
            "static class " + name + "\n{\n" + string.Join('\n', Enumerable.Range(0, 20).Select(i => $"    public const string {word}{i} = \"{word} {i}\";")) + "\n}\n";

        Scan("src/Words.cs", Table("Words", "Hello"));
        var findings = Scan("src/Steps.cs", Table("Steps", "Step"));

        Assert.DoesNotContain(findings, f => f.Rule == "DuplicateCode");
    }

    [Fact]
    public void RepetitiveCodeInOneStretchIsNotACopyOfItself()
    {
        var lines = string.Join('\n', Enumerable.Range(0, 60).Select(i => $"        list.Add({i});"));

        var findings = Scan("src/Many.cs", "class Many\n{\n    void M(System.Collections.Generic.List<int> list)\n    {\n" + lines + "\n    }\n}\n");

        Assert.DoesNotContain(findings, f => f.Rule == "DuplicateCode");
    }

    [Fact]
    public void ChurnIsReadFromGitNumstatAndRankedByCommitsTimesPressure()
    {
        var churn = Hotspots.Parse("@@aaa\n10\t2\tsrc/A.cs\n3\t0\tsrc/B.cs\n\n@@bbb\n5\t5\tsrc/A.cs\n-\t-\tlogo.png\n");
        var findings = new[] { new Finding("LongMethod", "src/A.cs", "A.M", 1, 120, "m"), new Finding("LongMethod", "src/B.cs", "B.M", 1, 120, "m"), new Finding("LongMethod", "src/C.cs", "C.M", 1, 300, "m") };

        var hot = Hotspots.Rank(churn, findings, top: 10);

        Assert.Equal((2, 22), churn["src/A.cs"]);
        Assert.Equal(new[] { "src/A.cs", "src/B.cs" }, hot.Select(h => h.File));   // C has findings but no recent changes
        Assert.Equal(4, hot[0].Score);
    }

    [Fact]
    public void ChecksAddedLaterAreLeftOutOfTheHeadlineComparison()
    {
        var counts = CheckCounts.From(new[] { new Finding("LongMethod", "a", "m", 1, 120, "m"), new Finding("DuplicateCode", "a", "d", 1, 20, "m") });

        var (total, pressure) = counts.Without(Rules.Newer);

        Assert.Equal(2, counts.Total);
        Assert.Equal(1, total);
        Assert.Equal(2, pressure);
    }

    [Fact]
    public void ADateDefaultOnADataPropertyIsNotAClockReadToFix_ButARealReadIs()
    {
        var findings = Scan("src/Models/Thing.cs", """
            using System;
            class Thing
            {
                public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
                public DateTime Expires { get; set; } = DateTime.UtcNow.AddDays(1);
                public bool IsOld() => DateTimeOffset.UtcNow - CreatedAt > TimeSpan.FromDays(1);
                void M() { var now = DateTime.Now; }
            }
            """);

        Assert.Equal(2, findings.Single(f => f.Rule == "DirectClock").Value);   // the two reads in methods, not the two defaults
    }

    // The samples are written with \u escapes so this test file stays ASCII; the scanned source holds the real characters.
    [Fact]
    public void A_non_ascii_character_in_a_string_char_or_interpolated_text_is_flagged_once_per_literal()
    {
        var findings = Scan("src/Text.cs", "class Text { string S = \"a…b\"; char C = 'é'; string I(int x) => $\"a – {x}\"; }");

        Assert.Equal(3, findings.Count(f => f.Rule == "NonAsciiLiteral"));
        Assert.Contains(findings, f => f.Rule == "NonAsciiLiteral" && f.Member == "Text literal@3");
    }

    [Fact]
    public void Verbatim_raw_comment_and_plain_literals_are_not_flagged()
    {
        var findings = Scan("src/Quiet.cs", "class Quiet { string V = @\"…\"; string R = \"\"\"…\"\"\"; string P = \"plain\"; } // … a comment\n");

        Assert.DoesNotContain(findings, f => f.Rule == "NonAsciiLiteral");
    }
}
