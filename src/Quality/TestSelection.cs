/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.RegularExpressions;
using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>The tests worth running for a change: per test project, the test classes to run; and the changed files no test mentions at all.</summary>
internal sealed record TestPlan(IReadOnlyDictionary<string, IReadOnlyList<string>> ClassesByProject, IReadOnlyList<string> NotMentioned)
{
    /// <summary>The functions of the site the change touches (when the site lists its functions): what a person would walk to see the change work.</summary>
    public IReadOnlyList<FeatureFunction> Functions { get; init; } = [];

    /// <summary>Why each selected test class was selected (class name -> the first reason found), so a selection that looks too big can be read instead of guessed at.</summary>
    public IReadOnlyDictionary<string, string> Why { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// A fast guess at which tests a change can break, by name: the types a changed file declares (and a Razor page's own name) are looked for in the test projects' source, and every test
/// class in a file that mentions one is selected; a changed test file selects its own classes. It is a shortcut for small iterations, not a proof - a test that reaches the code only
/// through the running app is not found by name - so the full suite remains the gate before a push.
/// </summary>
internal static partial class TestSelection
{
    /// <summary>Names shorter than this are too common to mean anything when found in a test (Id, Url, Map).</summary>
    private const int MinNameLength = 4;

    [GeneratedRegex(@"\b(?:class|record|struct|interface|enum)\s+(?<name>[A-Za-z_]\w*)")]
    private static partial Regex Declaration();

    // Comments and literals, found left to right so a quote inside a comment or a slash pair inside a string is not mistaken for the start of the other. Words in them ("a class that ...") are not declarations.
    [GeneratedRegex(@"""""""[\s\S]*?""""""|@""(?:[^""]|"""")*""|\$?""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])'|/\*[\s\S]*?\*/|//[^\n]*|@\*[\s\S]*?\*@|<!--[\s\S]*?-->")]
    private static partial Regex CommentsAndLiterals();

    /// <summary>The code of a file with its comments and literals blanked; of a Razor file, only its <c>@code</c> block (the markup is prose and tags, and "a class of its own" there is not a class).</summary>
    internal static string CodeOnly(string relativePath, string text)
    {
        var stripped = CommentsAndLiterals().Replace(text, " ");
        if (!relativePath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
        {
            return stripped;
        }

        var at = stripped.IndexOf("@code", StringComparison.Ordinal);
        return at < 0 ? string.Empty : stripped[at..];
    }

    /// <summary>The type names a file declares; a Razor page or component is also known by its file name.</summary>
    internal static IReadOnlyList<string> DeclaredNames(string relativePath, string text)
    {
        var names = Declaration().Matches(CodeOnly(relativePath, text)).Select(m => m.Groups["name"].Value).ToList();
        if (relativePath.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
        {
            names.Add(Path.GetFileNameWithoutExtension(relativePath));
        }

        return names.Where(n => n.Length >= MinNameLength).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Test classes among a file's declarations: those in a file that holds at least one test (a [Fact], [Theory] or the site's own variants such as [BrowserFact]).</summary>
    internal static IReadOnlyList<string> TestClasses(string text)
    {
        var code = CommentsAndLiterals().Replace(text, " ");
        return Regex.IsMatch(code, @"\[(?:\w+\.)?\w*(?:Fact|Theory)\b")
            ? Declaration().Matches(code).Select(m => m.Groups["name"].Value).Distinct(StringComparer.Ordinal).ToList()
            : [];
    }

    [GeneratedRegex(@"@page\s+""(?<route>/[^""{]*)")]
    private static partial Regex PageDirective();

    /// <summary>
    /// For a changed Razor component or page: the routes of the pages that show it (the page itself if it has an <c>@page</c>, else the pages that use it as a tag, up to a few levels
    /// up). A test that requests one of those addresses exercises the change even though it never names the component - the usual shape of tests that host the real app.
    /// Routes shorter than 3 characters (the home page) would match nearly anything and are left out.
    /// </summary>
    internal static IReadOnlyList<string> RoutesShowing(string root, IReadOnlyList<string> sourceFolders, string changedRazor)
    {
        var razor = sourceFolders.Select(f => Path.Combine(root, f)).Where(Directory.Exists)
            .SelectMany(f => Directory.EnumerateFiles(f, "*.razor", SearchOption.AllDirectories))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToDictionary(p => p, File.ReadAllText);
        var routes = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<(string Name, int Depth)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFileNameWithoutExtension(changedRazor) };
        pending.Enqueue((Path.GetFileNameWithoutExtension(changedRazor), 0));
        foreach (var own in razor.Where(r => Path.GetFullPath(r.Key).EndsWith(changedRazor.Replace('/', Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)))
        {
            AddRoutes(own.Value, routes);
        }

        while (pending.Count > 0)
        {
            var (name, depth) = pending.Dequeue();
            foreach (var (path, text) in razor.Where(r => Regex.IsMatch(r.Value, $@"<{Regex.Escape(name)}[\s/>]")))
            {
                var user = Path.GetFileNameWithoutExtension(path);
                AddRoutes(text, routes);
                if (depth < 3 && seen.Add(user))
                {
                    pending.Enqueue((user, depth + 1));
                }
            }
        }

        return routes.Where(r => r.Length >= 3).OrderBy(r => r, StringComparer.Ordinal).ToList();
    }

    private static void AddRoutes(string razorText, ISet<string> routes)
    {
        foreach (Match match in PageDirective().Matches(razorText))
        {
            routes.Add(match.Groups["route"].Value.TrimEnd('/'));
        }
    }

    /// <summary>Names every web test mentions because it hosts the app (<c>WebApplicationFactory&lt;Program&gt;</c>): found in a test they say nothing about what it covers.</summary>
    private static readonly HashSet<string> Ubiquitous = new(["Program"], StringComparer.Ordinal);

    /// <summary>
    /// The tests for a change, chosen by what the change is about rather than by who happens to mention it. A changed page or component stands for the addresses it draws and a changed C# file for the addresses it
    /// names; the site's function list says which function owns each and which test classes are about it. Types a changed C# file declares still select the tests that name them (unit tests), but a test that only
    /// passes through a page on its way somewhere else (every flow test signs in) is not selected for it. A file that reaches no function and no test is listed, not run for.
    /// </summary>
    internal static TestPlan PlanByFunction(string root, IReadOnlyList<ChangedSource> changed, IReadOnlyList<string> testProjects, IReadOnlyList<string> sourceFolders, FeatureRegistryFile registry)
    {
        var wanted = changed.Where(c => !c.IsTest).Select(c => (c.Relative, Text: File.ReadAllText(Path.Combine(root, c.Relative)))).ToList();
        var touched = new List<FeatureFunction>();
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var addressesByFile = wanted.ToDictionary(w => w.Relative, w => AddressesOf(root, sourceFolders, w.Relative, w.Text));
        foreach (var (file, addresses) in addressesByFile)
        {
            var owners = FunctionSelection.Owning(registry, addresses);
            touched.AddRange(owners);
            if (owners.Any(o => o.Tests.Length > 0))
            {
                reached.Add(file);
            }
        }

        var functionTests = touched.SelectMany(f => f.Tests).Select(t => t.Split('.')[0]).ToHashSet(StringComparer.Ordinal);
        var names = wanted.Where(w => w.Relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Select(w => (w.Relative, Names: DeclaredNames(w.Relative, w.Text).Where(n => !Ubiquitous.Contains(n)).ToList())).ToList();
        var changedTests = changed.Where(c => c.IsTest).Select(c => Path.GetFullPath(Path.Combine(root, c.Relative))).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var byProject = new Dictionary<string, IReadOnlyList<string>>();
        var why = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var project in testProjects)
        {
            var classes = new List<string>();
            foreach (var path in TestSources(Path.GetDirectoryName(project)!))
            {
                var text = File.ReadAllText(path);
                var tests = TestClasses(text);
                var direct = tests.Count == 0 ? [] : names.Select(n => (n.Relative, Hit: n.Names.FirstOrDefault(name => Mentions(text, name)))).Where(n => n.Hit is not null).ToList();
                var byFunction = tests.FirstOrDefault(functionTests.Contains);
                var edited = changedTests.Contains(Path.GetFullPath(path));
                if (tests.Count > 0 && (direct.Count > 0 || byFunction is not null || edited))
                {
                    classes.AddRange(tests);
                    reached.UnionWith(direct.Select(d => d.Relative));
                    var reason = direct.Count > 0 ? $"mentions {direct[0].Hit} (from {Path.GetFileName(direct[0].Relative)})" : byFunction is not null ? $"named by a function of the site ({byFunction})" : "the test file itself changed";
                    foreach (var test in tests)
                    {
                        why.TryAdd(test, reason);
                    }
                }
            }

            if (classes.Count > 0)
            {
                byProject[project] = classes.Distinct(StringComparer.Ordinal).ToList();
            }
        }

        return new TestPlan(byProject, wanted.Select(w => w.Relative).Where(f => !reached.Contains(f)).ToList()) { Functions = touched.DistinctBy(f => f.Id).ToList(), Why = why };
    }

    /// <summary>The addresses a changed file stands for: a page its own and the pages that show it (the home page counts), a C# file the routes it names.</summary>
    private static IReadOnlyList<string> AddressesOf(string root, IReadOnlyList<string> sourceFolders, string relative, string text)
    {
        if (!relative.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
        {
            return FunctionSelection.RoutesNamed(text);
        }

        var own = PageDirective().Matches(text).Select(m => m.Groups["route"].Value.TrimEnd('/')).Select(r => r.Length == 0 ? "/" : r);
        return [.. own.Concat(RoutesShowing(root, sourceFolders, relative)).Distinct(StringComparer.Ordinal)];
    }

    internal static TestPlan Plan(string root, IReadOnlyList<ChangedSource> changed, IReadOnlyList<string> testProjects, IReadOnlyList<string>? sourceFolders = null, FeatureRegistryFile? registry = null)
    {
        if (registry is not null && sourceFolders is not null)
        {
            return PlanByFunction(root, changed, testProjects, sourceFolders, registry);
        }

        var wanted = changed.Where(c => !c.IsTest)
            .Select(c => (c.Relative, Names: DeclaredNames(c.Relative, File.ReadAllText(Path.Combine(root, c.Relative))),
                Routes: sourceFolders is not null && c.Relative.EndsWith(".razor", StringComparison.OrdinalIgnoreCase) ? RoutesShowing(root, sourceFolders, c.Relative) : []))
            .ToList();
        var changedTests = changed.Where(c => c.IsTest).Select(c => Path.GetFullPath(Path.Combine(root, c.Relative))).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var byProject = new Dictionary<string, IReadOnlyList<string>>();
        var mentioned = new HashSet<string>(StringComparer.Ordinal);
        foreach (var project in testProjects)
        {
            var classes = new List<string>();
            foreach (var path in TestSources(Path.GetDirectoryName(project)!))
            {
                var text = File.ReadAllText(path);
                var tests = TestClasses(text);
                if (tests.Count == 0)
                {
                    continue;
                }

                var hits = wanted.Where(w => w.Names.Any(name => Mentions(text, name)) || w.Routes.Any(route => text.Contains("\"" + route, StringComparison.Ordinal)))
                    .Select(w => w.Relative).ToList();
                if (hits.Count > 0 || changedTests.Contains(Path.GetFullPath(path)))
                {
                    classes.AddRange(tests);
                    mentioned.UnionWith(hits);
                }
            }

            if (classes.Count > 0)
            {
                byProject[project] = classes.Distinct(StringComparer.Ordinal).ToList();
            }
        }

        return new TestPlan(byProject, wanted.Select(w => w.Relative).Where(f => !mentioned.Contains(f)).ToList());
    }

    /// <summary>The dotnet test filter that runs just these classes (xunit: <c>FullyQualifiedName~A|FullyQualifiedName~B</c>).</summary>
    /// <remarks>A test's full name is Namespace.Class.Method (Outer+Inner for a nested class), so the class is looked for between its separators: a helper called "Environment" must not run every test that has the word somewhere in its name.</remarks>
    internal static string Filter(IEnumerable<string> classes) => string.Join("|", classes.SelectMany(c => new[] { $"FullyQualifiedName~.{c}.", $"FullyQualifiedName~+{c}." }));

    private static bool Mentions(string text, string name) => Regex.IsMatch(text, $@"\b{Regex.Escape(name)}\b");

    private static IEnumerable<string> TestSources(string projectFolder) =>
        Directory.EnumerateFiles(projectFolder, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
}
