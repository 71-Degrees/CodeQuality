/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Refactor;

/// <summary>What a run did (or, in a dry run, would do), printed per rule and per file.</summary>
internal sealed class RunSummary
{
    private readonly List<(string File, FileEdit Edit)> _files = new();

    public void Add(string file, FileEdit edit) => _files.Add((file, edit));

    public void Print(bool applied)
    {
        var verb = applied ? "Fixed" : "Would fix";
        Console.WriteLine();
        foreach (var (file, edit) in _files.Where(f => f.Edit.Fixed.Count > 0).OrderBy(f => f.File))
        {
            Console.WriteLine($"  {verb} {edit.Fixed.Count,3} in {file.Replace('\\', '/')}  ({string.Join(", ", edit.Fixed.GroupBy(w => w.Rule).Select(g => $"{g.Key} x{g.Count()}"))})");
        }

        var fixedByRule = _files.SelectMany(f => f.Edit.Fixed).GroupBy(w => w.Rule).OrderBy(g => g.Key);
        var leftByRule = _files.SelectMany(f => f.Edit.Left).GroupBy(w => w.Rule).OrderBy(g => g.Key).ToList();
        Console.WriteLine();
        Console.WriteLine($"{verb} {_files.Sum(f => f.Edit.Fixed.Count)} warning(s) in {_files.Count(f => f.Edit.Fixed.Count > 0)} file(s): {string.Join(", ", fixedByRule.Select(g => $"{g.Key} {g.Count()}"))}");
        PrintLeft(leftByRule);
        Console.WriteLine(applied ? "Next: build, run the tests, review the diff, commit." : "Dry run: nothing was changed. Add --apply to make these changes.");
    }

    private static void PrintLeft(List<IGrouping<string, Warning>> leftByRule)
    {
        if (leftByRule.Count == 0)
        {
            return;
        }

        Console.WriteLine($"Left for a person ({leftByRule.Sum(g => g.Count())}): {string.Join(", ", leftByRule.Select(g => $"{g.Key} {g.Count()}"))}");
        foreach (var warning in leftByRule.SelectMany(g => g).Take(15))
        {
            Console.WriteLine($"    {warning.Rule} {warning.File}({warning.Line},{warning.Column})");
        }
    }
}
