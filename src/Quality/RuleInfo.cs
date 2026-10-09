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
        ["DuplicateCode"] = new("The same code in two places", "Copy and paste means a bug fix or a rule change has to be made twice, and one copy gets forgotten. One place should know each rule.", "Extract the shared part into one method or class both places call; the tests prove nothing changed.", Automation.Assisted),
        ["MutableStatic"] = new("Static state that can change", "Hidden shared state makes behaviour depend on what ran before and breaks tests that run side by side.", "Pass the state in through the constructor, or make it a readonly immutable value.", Automation.Manual),
        ["DirectClock"] = new("Reads the system clock directly", "Code that asks the machine for the time cannot be tested at a chosen moment and scatters a side effect through the logic.", "Take a TimeProvider through the constructor (the fake one in tests); read the clock only at the edges.", Automation.Assisted),
        ["DirectHttpClient"] = new("Creates an HttpClient by hand", "Each one holds its own connections (exhausting sockets under load) and cannot be swapped for a fake in tests.", "Take an IHttpClientFactory or a typed client from dependency injection.", Automation.Assisted),
        ["DroppedTask"] = new("A started task nobody observes", "`_ = DoAsync()` throws away the task: if it fails, nothing logs it, and the process carries on as if it worked.", "Await it, or hand it to a hosted service or queue that logs failures; if it truly is fire-and-forget, log inside it.", Automation.Manual),
        ["TestWithoutAssert"] = new("A test that asserts nothing", "It can only fail by throwing, so it proves little and gives false confidence.", "Assert the result or the effect it exists to prove, or delete it.", Automation.Manual),
        ["SkippedTest"] = new("A test marked to be skipped", "A skipped test is a promise nobody is keeping.", "Fix it and enable it, or delete it.", Automation.Manual),
        ["build/CA1816"] = new("Dispose should suppress finalization", "Disposable types should call GC.SuppressFinalize(this) in Dispose().", "Add the call.", Automation.Mechanical),
        ["build/CA1305"] = new("Culture-dependent formatting or parsing", "int.Parse / string formatting behaves differently on a machine with another locale.", "Pass CultureInfo.InvariantCulture (or the right culture).", Automation.Mechanical),
        ["build/CA1001"] = new("Type owns a disposable but is not disposable", "A field holds something that must be disposed, and nothing disposes it.", "Implement IDisposable on the owner and dispose the field.", Automation.Assisted),
        ["build/CA1859"] = new("Use the concrete type for speed", "Declared as an interface or base type where the concrete type is always used.", "Change the declared type.", Automation.Mechanical),
        ["build/CA1852"] = new("Class can be sealed", "No subclasses exist; sealing is clearer and slightly faster.", "Add 'sealed'.", Automation.Mechanical),
        ["build/CA1822"] = new("Member can be static", "It uses no instance data.", "Add 'static'.", Automation.Mechanical),
        ["build/CA1310"] = new("String comparison depends on culture", "A string method compares using the current culture by default.", "Pass StringComparison.Ordinal (or OrdinalIgnoreCase).", Automation.Mechanical),
        ["build/CA1826"] = new("Use indexing instead of LINQ", "First()/Last() on a list or array where an index works.", "Use [0] / [^1] / Count.", Automation.Mechanical),
    };

    /// <summary>The smell rules added after the first recording on 2026-10-09. The timeline's headline numbers leave them out so that adding a check does not look like quality getting worse; they have their own row.</summary>
    public static readonly IReadOnlyList<string> Newer = new[] { "DuplicateCode", "MutableStatic", "DirectClock", "DirectHttpClient", "DroppedTask", "TestWithoutAssert", "SkippedTest" };

    public static RuleInfo For(string rule) =>
        Known.TryGetValue(rule, out var info) ? info : new RuleInfo(rule, "A .NET analyzer rule (see Microsoft's code-quality rule documentation for this id).", "See the analyzer's own message.", rule.StartsWith("build/", StringComparison.Ordinal) ? Automation.Mechanical : Automation.Manual);
}
