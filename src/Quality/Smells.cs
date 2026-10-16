/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>
/// Reads our own C# (syntax only - no build needed, so it runs in seconds and cannot be broken by a half-finished change) and reports code smells: methods that are too long, too
/// branchy, too deeply nested or take too many parameters; files that have grown too big; error handling that hides problems; blocking on async work. It also measures Razor pages
/// (file length and the size of the <c>@code</c> block), which the C# tools cannot see. A smell is a lead for a person, not a verdict.
/// </summary>
internal static class Smells
{
    private static readonly string[] SkipFolders = { "/obj/", "/bin/", "/node_modules/", "/wwwroot/lib/" };

    public static IReadOnlyList<Finding> Scan(string root, bool includeTests, ToolConfig config)
    {
        var findings = new List<Finding>();
        var streams = new List<TokenStream>();
        foreach (var (path, relative, isTest) in SourceFiles(root, config))
        {
            var text = File.ReadAllText(path);
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                if (!isTest || includeTests)
                {
                    ScanRazor(relative, text, config.Limits, findings);
                }

                continue;
            }

            var tree = CSharpSyntaxTree.ParseText(text);
            ScanCSharp(relative, tree, config.Limits, findings, isTest ? (includeTests ? FileKind.TestFull : FileKind.TestRulesOnly) : FileKind.Code);
            if (!isTest || includeTests)
            {
                streams.Add(Duplicates.Stream(relative, tree));
            }
        }

        findings.AddRange(Duplicates.Find(streams, config.Limits));
        return Applicable(findings, config);
    }

    /// <summary>Drops the rules a site switched off, and each rule's findings in the places where it does not apply (the app's edges).</summary>
    private static List<Finding> Applicable(List<Finding> findings, ToolConfig config) =>
        findings.Where(f => !config.DisabledRules.Contains(f.Rule)
            && !(config.RuleExemptions.TryGetValue(f.Rule, out var places) && places.Any(place => ("/" + f.File).Contains(place, StringComparison.Ordinal)))).ToList();

    /// <summary>The size of every source file we scan, for the report's "where is the code" view.</summary>
    public static IReadOnlyList<(string File, int Lines)> CountLines(string root, bool includeTests, ToolConfig config) =>
        SourceFiles(root, config).Where(f => includeTests || !f.IsTest).Select(f => (f.Relative, File.ReadLines(f.Path).Count())).ToList();

    /// <summary>Which rules apply to a C# file: application code, test code (only the test rules unless tests were asked for), or both.</summary>
    private enum FileKind { Code, TestFull, TestRulesOnly }

    /// <summary>Every .cs and .razor file under the source and test folders, leaving out build output and generated code.</summary>
    private static IEnumerable<(string Path, string Relative, bool IsTest)> SourceFiles(string root, ToolConfig config)
    {
        var folders = config.SourceFolders.Select(f => (Folder: f, IsTest: false)).Concat(config.TestFolders.Select(f => (Folder: f, IsTest: true)));
        foreach (var (folder, isTest) in folders.Select(f => (Path.Combine(root, f.Folder), f.IsTest)).Where(f => Directory.Exists(f.Item1)))
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                var isSource = path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".razor", StringComparison.OrdinalIgnoreCase);
                if (isSource && !IsSkipped(relative, config))
                {
                    yield return (path, relative, isTest);
                }
            }
        }
    }

    /// <summary>True for build output, vendored libraries, generated code and anything the site's quality.json skips. Shared with <c>quality quick</c> so both look at the same files.</summary>
    internal static bool IsSkipped(string relative, ToolConfig config) =>
        SkipFolders.Concat(config.Skipped).Any(("/" + relative).Contains) || relative.EndsWith(".g.cs") || relative.EndsWith(".Designer.cs");

    private static void ScanRazor(string file, string text, Thresholds limits, List<Finding> findings)
    {
        var lines = text.Split('\n').Length;
        if (lines > limits.FileLines)
        {
            findings.Add(new Finding("LongFile", file, "(file)", 1, lines, $"{lines} lines (limit {limits.FileLines}): split it into smaller components."));
        }

        var at = text.IndexOf("@code", StringComparison.Ordinal);
        if (at >= 0)
        {
            var codeLines = text[at..].Split('\n').Length;
            if (codeLines > limits.RazorCodeLines)
            {
                findings.Add(new Finding("RazorCodeBlock", file, "@code", text[..at].Count(c => c == '\n') + 1, codeLines,
                    $"The @code block is {codeLines} lines (limit {limits.RazorCodeLines}): move logic into a service or a code-behind class that can be tested."));
            }
        }
    }

    private static void ScanCSharp(string file, SyntaxTree tree, Thresholds limits, List<Finding> findings, FileKind kind)
    {
        var root = tree.GetRoot();
        if (kind != FileKind.Code)
        {
            StandardRules.CheckTests(file, root, tree, findings);
        }

        if (kind == FileKind.TestRulesOnly)
        {
            return;
        }

        var scan = new FileScan(file, tree, limits, findings);
        var lineCount = tree.GetText().Lines.Count;
        if (lineCount > limits.FileLines)
        {
            findings.Add(new Finding("LongFile", file, "(file)", 1, lineCount, $"{lineCount} lines (limit {limits.FileLines}): split it up."));
        }

        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case MethodDeclarationSyntax m:
                    CheckBody(scan, m, Name(m), m.ParameterList.Parameters.Count, (SyntaxNode?)m.Body ?? m.ExpressionBody);
                    CheckAsyncVoid(file, tree, m, findings);
                    break;
                case ConstructorDeclarationSyntax c:
                    CheckBody(scan, c, Name(c), c.ParameterList.Parameters.Count, (SyntaxNode?)c.Body ?? c.ExpressionBody);
                    break;
                case LocalFunctionStatementSyntax f:
                    CheckBody(scan, f, "local " + f.Identifier.Text + " in " + Name(f), f.ParameterList.Parameters.Count, (SyntaxNode?)f.Body ?? f.ExpressionBody);
                    break;
                case LambdaExpressionSyntax { Block: not null } l:
                    CheckBody(scan, l, LambdaName(l), ParameterCount(l), l.Block);
                    break;
                case CatchClauseSyntax c:
                    CheckCatch(file, tree, c, findings);
                    break;
                case InvocationExpressionSyntax i:
                    CheckBlocking(file, tree, i, findings);
                    break;
            }
        }

        CheckTodoMarkers(root, tree, file, findings);
        if (kind == FileKind.Code)
        {
            StandardRules.CheckCode(file, root, tree, findings);
        }
    }

    private static void CheckTodoMarkers(SyntaxNode root, SyntaxTree tree, string file, List<Finding> findings)
    {
        foreach (var trivia in root.DescendantTrivia().Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia)))
        {
            var comment = trivia.ToString();
            if (comment.Contains("TODO") || comment.Contains("FIXME") || comment.Contains("HACK"))
            {
                var line = tree.GetLineSpan(trivia.Span).StartLinePosition.Line + 1;
                findings.Add(new Finding("TodoMarker", file, "todo: " + comment.Trim()[..Math.Min(60, comment.Trim().Length)], line, 1, "A TODO/FIXME/HACK marker: " + comment.Trim()));
            }
        }
    }

    /// <summary>What a scan of one file carries around: the file, its parsed tree, the limits, and the list findings are added to.</summary>
    private sealed record FileScan(string File, SyntaxTree Tree, Thresholds Limits, List<Finding> Findings);

    private static void CheckBody(FileScan scan, SyntaxNode owner, string name, int parameters, SyntaxNode? body)
    {
        if (body is null)
        {
            return;
        }

        var (file, tree, limits, findings) = scan;
        var span = tree.GetLineSpan(body.Span);
        var line = span.StartLinePosition.Line + 1;
        var lines = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
        if (lines > limits.MethodLines)
        {
            findings.Add(new Finding("LongMethod", file, name, line, lines, $"{name} is {lines} lines (limit {limits.MethodLines}): extract the steps into named methods."));
        }

        var complexity = 1 + Own(body).Count(IsBranch);
        if (complexity > limits.Complexity)
        {
            findings.Add(new Finding("Complexity", file, name, line, complexity, $"{name} has {complexity} decision points (limit {limits.Complexity}): hard to test and easy to break."));
        }

        var depth = Depth(body, 0);
        if (depth > limits.Nesting)
        {
            findings.Add(new Finding("DeepNesting", file, name, line, depth, $"{name} nests {depth} levels deep (limit {limits.Nesting}): use early returns or extract the inner block."));
        }

        if (parameters > limits.Parameters && owner is not LambdaExpressionSyntax)
        {
            findings.Add(new Finding("ManyParameters", file, name, line, parameters, $"{name} takes {parameters} parameters (limit {limits.Parameters}): group them into an options object."));
        }
    }

    /// <summary>Nodes of this body only: a nested lambda or local function is measured on its own, not added to its parent.</summary>
    private static IEnumerable<SyntaxNode> Own(SyntaxNode body)
    {
        var stack = new Stack<SyntaxNode>();
        stack.Push(body);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            foreach (var child in node.ChildNodes())
            {
                if (child is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax)
                {
                    continue;
                }

                yield return child;
                stack.Push(child);
            }
        }
    }

    private static bool IsBranch(SyntaxNode n) => n switch
    {
        IfStatementSyntax or WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or CatchClauseSyntax
            or ConditionalExpressionSyntax or SwitchExpressionArmSyntax or CaseSwitchLabelSyntax or CasePatternSwitchLabelSyntax => true,
        BinaryExpressionSyntax b => b.IsKind(SyntaxKind.LogicalAndExpression) || b.IsKind(SyntaxKind.LogicalOrExpression) || b.IsKind(SyntaxKind.CoalesceExpression),
        _ => false,
    };

    private static int Depth(SyntaxNode node, int current)
    {
        var max = current;
        foreach (var child in node.ChildNodes())
        {
            if (child is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax or LocalFunctionStatementSyntax)
            {
                continue;
            }

            var next = child is IfStatementSyntax { Parent: ElseClauseSyntax } ? current   // else-if is not deeper
                : child is IfStatementSyntax or WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or SwitchStatementSyntax
                    or TryStatementSyntax or UsingStatementSyntax or LockStatementSyntax ? current + 1 : current;
            max = Math.Max(max, Depth(child, next));
        }

        return max;
    }

    private static void CheckAsyncVoid(string file, SyntaxTree tree, MethodDeclarationSyntax m, List<Finding> findings)
    {
        if (!m.Modifiers.Any(SyntaxKind.AsyncKeyword) || m.ReturnType is not PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword })
        {
            return;
        }

        var first = m.ParameterList.Parameters.FirstOrDefault()?.Identifier.Text;
        if (first is "sender" or "state" or "o")
        {
            return;   // an event handler or timer callback has no other shape
        }

        findings.Add(new Finding("AsyncVoid", file, Name(m), tree.GetLineSpan(m.Span).StartLinePosition.Line + 1, 1, $"{Name(m)} is `async void`: an exception in it cannot be caught and can take the process down. Return Task."));
    }

    private static void CheckCatch(string file, SyntaxTree tree, CatchClauseSyntax c, List<Finding> findings)
    {
        var line = tree.GetLineSpan(c.Span).StartLinePosition.Line + 1;
        var owner = Name(c);
        if (c.Block.Statements.Count == 0)
        {
            findings.Add(new Finding("EmptyCatch", file, owner + " catch@" + Ordinal(c), line, 1, "An empty catch block swallows the error with no trace: log it, handle it, or say why it is safe to ignore."));
            return;
        }

        var broad = c.Declaration is null || c.Declaration.Type.ToString() is "Exception" or "System.Exception";
        if (!broad || c.Filter is not null)
        {
            return;
        }

        if (!RethrowsOrLogs(c.Block))
        {
            findings.Add(new Finding("BroadCatch", file, owner + " catch@" + Ordinal(c), line, 1, "Catches every exception without logging or rethrowing: real bugs will pass silently."));
        }
    }

    /// <summary>A catch-all is acceptable when it rethrows or leaves a trace: a throw, a call to a logger (<c>Log...</c>), a write to <c>Console.Error</c> or Serilog's <c>SelfLog</c>, or the alert hook the
    /// dev tools use. Mentioning the word "Failed" or "Error" in a status assignment is not a trace.</summary>
    private static bool RethrowsOrLogs(BlockSyntax block) =>
        block.DescendantNodes().OfType<ThrowStatementSyntax>().Any()
        || block.DescendantNodes().OfType<ThrowExpressionSyntax>().Any()
        || block.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call =>
            call.Expression.ToString() is var text
            && (InvokedName(call).StartsWith("Log", StringComparison.Ordinal)
                || text.StartsWith("Console.Error", StringComparison.Ordinal)
                || text.StartsWith("SelfLog.", StringComparison.Ordinal)
                || text.Contains("onSendFailure", StringComparison.Ordinal)));

    private static string InvokedName(InvocationExpressionSyntax call) => call.Expression switch
    {
        MemberAccessExpressionSyntax access => access.Name.Identifier.Text,
        IdentifierNameSyntax identifier => identifier.Identifier.Text,
        _ => string.Empty,
    };

    private static void CheckBlocking(string file, SyntaxTree tree, InvocationExpressionSyntax i, List<Finding> findings)
    {
        var text = i.Expression.ToString();
        var blocking = text.EndsWith(".GetAwaiter().GetResult", StringComparison.Ordinal) || (text.EndsWith(".Wait", StringComparison.Ordinal) && i.ArgumentList.Arguments.Count == 0);
        if (blocking)
        {
            findings.Add(new Finding("BlockingAsync", file, Name(i) + " @" + Ordinal(i), tree.GetLineSpan(i.Span).StartLinePosition.Line + 1, 1,
                "Blocks a thread waiting for a task (" + text + "): use await, or on a request path it can stall the server."));
        }
    }

    // ---- naming: stable across edits (never a line number) ----

    private static string Name(SyntaxNode node)
    {
        var parts = new List<string>();
        for (var n = node; n is not null; n = n.Parent!)
        {
            switch (n)
            {
                case MethodDeclarationSyntax m: parts.Add($"{m.Identifier.Text}({m.ParameterList.Parameters.Count})"); break;
                case ConstructorDeclarationSyntax c: parts.Add($"{c.Identifier.Text}.ctor({c.ParameterList.Parameters.Count})"); break;
                case LocalFunctionStatementSyntax f when n != node: parts.Add(f.Identifier.Text); break;
                case TypeDeclarationSyntax t: parts.Add(t.Identifier.Text); break;
                case PropertyDeclarationSyntax p: parts.Add(p.Identifier.Text); break;
            }
        }

        if (parts.Count == 0)
        {
            return "(top-level)";
        }

        parts.Reverse();   // found innermost first; read outermost first: "NearbyDigestService.RunOnce(1)"
        return string.Join(".", parts);
    }

    private static string LambdaName(LambdaExpressionSyntax l)
    {
        for (var n = l.Parent; n is not null; n = n.Parent)
        {
            if (n is InvocationExpressionSyntax inv && inv.ArgumentList.Arguments.FirstOrDefault()?.Expression is LiteralExpressionSyntax { Token.Value: string route })
            {
                return $"{inv.Expression.ToString().Split('.').Last()} \"{route}\"";
            }

            if (n is MethodDeclarationSyntax)
            {
                break;
            }
        }

        return "lambda in " + Name(l);
    }

    private static int ParameterCount(LambdaExpressionSyntax l) => l switch
    {
        ParenthesizedLambdaExpressionSyntax p => p.ParameterList.Parameters.Count,
        _ => 1,
    };

    /// <summary>The place of a node among its siblings of the same kind inside the enclosing member, so two catch blocks in one method get different, stable names.</summary>
    private static int Ordinal(SyntaxNode node)
    {
        var member = node.Ancestors().FirstOrDefault(a => a is MethodDeclarationSyntax or ConstructorDeclarationSyntax or LocalFunctionStatementSyntax or GlobalStatementSyntax or CompilationUnitSyntax);
        return member is null ? 0 : member.DescendantNodes().Where(n => n.GetType() == node.GetType()).TakeWhile(n => n != node).Count() + 1;
    }
}
