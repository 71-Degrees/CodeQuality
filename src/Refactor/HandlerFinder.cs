/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeQuality.Refactor;

/// <summary>Finds the inline-lambda route registrations in a file.</summary>
internal static partial class HandlerFinder
{
    [GeneratedRegex("^Map(Get|Post|Put|Delete|Patch)$")]
    private static partial Regex MapName();

    public static IReadOnlyList<Handler> Find(SyntaxNode root)
    {
        var handlers = new List<Handler>();
        foreach (var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (AsHandler(call) is { } handler)
            {
                handlers.Add(handler);
            }
        }

        return handlers;
    }

    private static Handler? AsHandler(InvocationExpressionSyntax call)
    {
        var name = SourceText.NameOf(call);
        var container = call.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        var arguments = call.ArgumentList.Arguments;
        if (name is null || container is null || !MapName().IsMatch(name) || arguments.Count < 2 || !IsDirectChild(container, call))
        {
            return null;
        }

        return arguments[0].Expression is LiteralExpressionSyntax route && arguments[1].Expression is LambdaExpressionSyntax lambda
            ? new Handler(container, name, route.Token.ValueText, lambda)
            : null;
    }

    /// <summary>True when the call belongs to the container method itself, not to a lambda or local function nested inside it.</summary>
    private static bool IsDirectChild(MethodDeclarationSyntax container, InvocationExpressionSyntax call) =>
        call.Ancestors().TakeWhile(a => a != container).All(a => a is not (LambdaExpressionSyntax or LocalFunctionStatementSyntax or AnonymousMethodExpressionSyntax));
}
