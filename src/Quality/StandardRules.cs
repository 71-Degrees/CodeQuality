/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeQuality.Quality;

/// <summary>
/// Smells for the rules a team writes down and then has to remember: no mutable static state, time and HTTP clients come in from the edges, a started task is never dropped on the floor, and a test that
/// does not assert anything proves nothing. Syntax only, like the other smells. Reads that are counted per file (the clock, new HttpClient, dropped tasks) are one finding per file whose value is the count,
/// so a file with twelve of them is one line in the baseline, and the ratchet fails when the count grows.
/// </summary>
internal static class StandardRules
{
    private static readonly string[] TestAttributes = { "Fact", "Theory", "Test", "TestMethod", "TestCase" };

    private static readonly string[] AssertionWords = { "Assert", "Verify", "Should", "Expect" };

    /// <summary>The rules for application code.</summary>
    public static void CheckCode(string file, SyntaxNode root, SyntaxTree tree, List<Finding> findings)
    {
        MutableStatics(file, root, tree, findings);
        CountPerFile(file, "DirectClock", root.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Count(IsClockRead),
            "reads of the system clock (DateTime/DateTimeOffset Now, UtcNow, Today): take a TimeProvider so the time can be controlled in a test", findings);
        CountPerFile(file, "DirectHttpClient", root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>().Count(IsHttpClient),
            "places that create an HttpClient directly: take an IHttpClientFactory (or a typed client) so connections are pooled and tests can substitute it", findings);
        CountPerFile(file, "DroppedTask", root.DescendantNodes().OfType<AssignmentExpressionSyntax>().Count(IsDroppedTask),
            "tasks started and dropped with `_ =`: a failure in them is never seen unless the task logs for itself", findings);
    }

    /// <summary>The rules for test code.</summary>
    public static void CheckTests(string file, SyntaxNode root, SyntaxTree tree, List<Finding> findings)
    {
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>().Where(IsTest))
        {
            var name = Owner(method) + "." + method.Identifier.Text;
            var line = tree.GetLineSpan(method.Span).StartLinePosition.Line + 1;
            if (!Asserts(method))
            {
                findings.Add(new Finding("TestWithoutAssert", file, name, line, 1, $"{name} is a test with no assertion: it can only fail by throwing, so it proves little."));
            }

            if (IsSkipped(method))
            {
                findings.Add(new Finding("SkippedTest", file, name, line, 1, $"{name} is marked to be skipped: fix it or delete it."));
            }
        }
    }

    private static void MutableStatics(string file, SyntaxNode root, SyntaxTree tree, List<Finding> findings)
    {
        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>().Where(IsMutableStaticField))
        {
            foreach (var variable in field.Declaration.Variables)
            {
                findings.Add(new Finding("MutableStatic", file, $"{Owner(field)}.{variable.Identifier.Text}", tree.GetLineSpan(variable.Span).StartLinePosition.Line + 1, 1,
                    $"{Owner(field)}.{variable.Identifier.Text} is static state that can change: hidden shared state makes behaviour depend on what ran before. Pass it in, or make it readonly and immutable."));
            }
        }

        foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Where(p => p.Modifiers.Any(SyntaxKind.StaticKeyword) && HasSetter(p)))
        {
            findings.Add(new Finding("MutableStatic", file, $"{Owner(property)}.{property.Identifier.Text}", tree.GetLineSpan(property.Span).StartLinePosition.Line + 1, 1,
                $"{Owner(property)}.{property.Identifier.Text} is a static property with a setter: hidden shared state. Pass it in, or make it get-only."));
        }
    }

    private static bool IsMutableStaticField(FieldDeclarationSyntax f)
    {
        if (!f.Modifiers.Any(SyntaxKind.StaticKeyword) || f.Modifiers.Any(SyntaxKind.ConstKeyword))
        {
            return false;
        }

        if (f.AttributeLists.SelectMany(a => a.Attributes).Any(a => a.Name.ToString().Contains("ThreadStatic", StringComparison.Ordinal)))
        {
            return false;   // per-thread, not shared
        }

        // readonly is only the reference: a static readonly ConcurrentDictionary is still shared mutable state.
        return !f.Modifiers.Any(SyntaxKind.ReadOnlyKeyword) || f.Declaration.Type.ToString().Contains("Concurrent", StringComparison.Ordinal);
    }

    private static bool HasSetter(PropertyDeclarationSyntax p) => p.AccessorList?.Accessors.Any(a => a.IsKind(SyntaxKind.SetAccessorDeclaration)) == true;

    private static bool IsClockRead(MemberAccessExpressionSyntax access) =>
        access.Name.Identifier.Text is "Now" or "UtcNow" or "Today"
        && access.Expression.ToString() is "DateTime" or "DateTimeOffset" or "System.DateTime" or "System.DateTimeOffset";

    private static bool IsHttpClient(BaseObjectCreationExpressionSyntax creation) =>
        creation is ObjectCreationExpressionSyntax { Type: var type } && type.ToString() is "HttpClient" or "System.Net.Http.HttpClient";

    private static bool IsDroppedTask(AssignmentExpressionSyntax assignment) =>
        assignment.Left is IdentifierNameSyntax { Identifier.Text: "_" }
        && assignment.Right is InvocationExpressionSyntax call
        && (call.Expression.ToString().StartsWith("Task.Run", StringComparison.Ordinal) || InvokedName(call).EndsWith("Async", StringComparison.Ordinal));

    private static string InvokedName(InvocationExpressionSyntax call) => call.Expression switch
    {
        MemberAccessExpressionSyntax access => access.Name.Identifier.Text,
        IdentifierNameSyntax identifier => identifier.Identifier.Text,
        _ => string.Empty,
    };

    private static void CountPerFile(string file, string rule, int count, string what, List<Finding> findings)
    {
        if (count > 0)
        {
            findings.Add(new Finding(rule, file, "(file)", 1, count, $"{count} {what}."));
        }
    }

    private static bool IsTest(MethodDeclarationSyntax method) =>
        method.AttributeLists.SelectMany(a => a.Attributes).Any(a => TestAttributes.Contains(AttributeName(a)));

    private static string AttributeName(AttributeSyntax attribute) => attribute.Name.ToString().Split('.').Last();

    private static bool IsSkipped(MethodDeclarationSyntax method) =>
        method.AttributeLists.SelectMany(a => a.Attributes)
            .Any(a => TestAttributes.Contains(AttributeName(a)) && a.ArgumentList?.Arguments.Any(arg => arg.NameEquals?.Name.Identifier.Text == "Skip") == true);

    private static bool Asserts(MethodDeclarationSyntax method) =>
        method.DescendantNodes().OfType<IdentifierNameSyntax>().Any(i => AssertionWords.Any(word => i.Identifier.Text.StartsWith(word, StringComparison.Ordinal)))
        || method.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call => InvokedName(call).StartsWith("Throws", StringComparison.Ordinal));

    private static string Owner(SyntaxNode node) => node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "(top-level)";
}
