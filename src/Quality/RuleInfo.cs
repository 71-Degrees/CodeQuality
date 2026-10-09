/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Quality;

/// <summary>How a finding can be fixed: <see cref="Mechanical"/> needs no judgment (a tool can apply it and the tests prove nothing changed), <see cref="Assisted"/> a tool can do the typing but a person picks
/// where or how, <see cref="Manual"/> is a decision about what the code should do.</summary>
internal enum Automation { Mechanical, Assisted, Manual }

internal sealed record RuleInfo(string Title, string Why, string Fix, Automation Automation);

/// <summary>Plain-language explanations shown in the report next to each rule.</summary>
internal static class Rules
{
    private static readonly Dictionary<string, RuleInfo> Known = new()
    {
        ["LongMethod"] = new("Method is too long", "Long methods hide several jobs in one place: hard to read, hard to test, and every change risks the other jobs.", "Extract each step into a well-named method. A tool can do the extraction once a person picks the range.", Automation.Assisted),
        ["Complexity"] = new("Too many branches", "Each decision multiplies the paths a test must cover; code above about 15 is where bugs hide.", "Extract branches into named methods, replace chains of if/else with a lookup or small classes, return early.", Automation.Assisted),
        ["DeepNesting"] = new("Nested too deep", "More than four levels of indentation is hard to follow.", "Use guard clauses (return early) and extract the inner block. Inverting an if is mechanical.", Automation.Assisted),
        ["ManyParameters"] = new("Too many parameters", "Callers must remember an order, and adding one more breaks every call.", "Group related values into a record/options object.", Automation.Assisted),
        ["LongFile"] = new("File is too big", "Big files mix concerns and make every merge and review slower.", "Split by responsibility: endpoint groups out of Program.cs, a page's logic out of its .razor file, types into their own files.", Automation.Assisted),
        ["RazorCodeBlock"] = new("Page logic inside the .razor file", "Logic in a Razor @code block cannot be unit-tested and the page grows without limit.", "Move the @code block into a partial class (Page.razor.cs) - mechanical - then pull real logic into services that tests can reach.", Automation.Assisted),
        ["EmptyCatch"] = new("Error swallowed silently", "An empty catch hides failures: the program carries on in a bad state and nobody knows.", "Decide per case: log it, handle it, narrow the exception type, or let it through. Some are deliberate (best-effort cleanup) and just need a comment and a log line.", Automation.Manual),
        ["BroadCatch"] = new("Catches every exception without logging", "Real bugs pass silently.", "Log the exception (and keep the catch if best-effort is intended), or catch the specific exceptions expected.", Automation.Assisted),
        ["AsyncVoid"] = new("async void method", "An exception in it cannot be caught and can take the process down.", "Return Task instead of void.", Automation.Mechanical),
        ["BlockingAsync"] = new("Blocks a thread waiting on a task", "On a request path this can stall the server and deadlock.", "Make the caller async and await.", Automation.Manual),
        ["TodoMarker"] = new("TODO / FIXME / HACK left in code", "A note to self that was never resolved.", "Do it, ticket it, or delete the comment.", Automation.Manual),
        ["build/CA1816"] = new("Dispose should suppress finalization", "Disposable types should call GC.SuppressFinalize(this) in Dispose().", "Add the call.", Automation.Mechanical),
        ["build/CA1305"] = new("Culture-dependent formatting or parsing", "int.Parse / string formatting behaves differently on a machine with another locale.", "Pass CultureInfo.InvariantCulture (or the right culture).", Automation.Mechanical),
        ["build/CA1001"] = new("Type owns a disposable but is not disposable", "A field holds something that must be disposed, and nothing disposes it.", "Implement IDisposable on the owner and dispose the field.", Automation.Assisted),
        ["build/CA1859"] = new("Use the concrete type for speed", "Declared as an interface or base type where the concrete type is always used.", "Change the declared type.", Automation.Mechanical),
        ["build/CA1852"] = new("Class can be sealed", "No subclasses exist; sealing is clearer and slightly faster.", "Add 'sealed'.", Automation.Mechanical),
        ["build/CA1822"] = new("Member can be static", "It uses no instance data.", "Add 'static'.", Automation.Mechanical),
        ["build/CA1310"] = new("String comparison depends on culture", "A string method compares using the current culture by default.", "Pass StringComparison.Ordinal (or OrdinalIgnoreCase).", Automation.Mechanical),
        ["build/CA1826"] = new("Use indexing instead of LINQ", "First()/Last() on a list or array where an index works.", "Use [0] / [^1] / Count.", Automation.Mechanical),
    };

    public static RuleInfo For(string rule) =>
        Known.TryGetValue(rule, out var info) ? info : new RuleInfo(rule, "A .NET analyzer rule (see Microsoft's code-quality rule documentation for this id).", "See the analyzer's own message.", rule.StartsWith("build/", StringComparison.Ordinal) ? Automation.Mechanical : Automation.Manual);
}
