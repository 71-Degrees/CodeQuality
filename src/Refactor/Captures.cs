/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeQuality.Refactor;

/// <summary>Which variables of the registering method a handler lambda uses. Found by name from the syntax alone: a name the lambda declares itself does not count; a handler's own locals shadow nothing.</summary>
internal static class Captures
{
    public static IReadOnlyList<string> Of(Handler handler)
    {
        var declaredOutside = DeclaredIn(handler.Container);
        var declaredInside = DeclaredInside(handler.Lambda);
        return handler.Lambda.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Select(i => i.Identifier.Text)
            .Where(n => declaredOutside.Contains(n) && !declaredInside.Contains(n))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The parameters and top-level locals and local functions of the method.</summary>
    private static HashSet<string> DeclaredIn(MethodDeclarationSyntax method)
    {
        var names = method.ParameterList.Parameters.Select(p => p.Identifier.Text).ToHashSet(StringComparer.Ordinal);
        foreach (var statement in method.Body?.Statements ?? default)
        {
            switch (statement)
            {
                case LocalDeclarationStatementSyntax local:
                    names.UnionWith(local.Declaration.Variables.Select(v => v.Identifier.Text));
                    break;
                case LocalFunctionStatementSyntax function:
                    names.Add(function.Identifier.Text);
                    break;
            }
        }

        return names;
    }

    private static HashSet<string> DeclaredInside(LambdaExpressionSyntax lambda)
    {
        var names = lambda.DescendantNodes().OfType<VariableDeclaratorSyntax>().Select(v => v.Identifier.Text).ToHashSet(StringComparer.Ordinal);
        names.UnionWith(lambda.DescendantNodes().OfType<ParameterSyntax>().Select(p => p.Identifier.Text));
        names.UnionWith(lambda.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Select(f => f.Identifier.Text));
        names.UnionWith(lambda.DescendantNodes().OfType<SingleVariableDesignationSyntax>().Select(d => d.Identifier.Text));
        return names;
    }
}
