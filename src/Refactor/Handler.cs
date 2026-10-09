/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeQuality.Refactor;

/// <summary>A route registration whose handler is an inline lambda: <c>app.MapPost("/x", async (HttpContext http) =&gt; { ... })</c>.</summary>
internal sealed record Handler(MethodDeclarationSyntax Container, string MapMethod, string Route, LambdaExpressionSyntax Lambda)
{
    public bool IsAsync => Lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword);

    /// <summary>The parameter list's text, without its parentheses; null when the lambda leaves its parameter types to be inferred (those cannot become a method).</summary>
    public string? ParameterText => Lambda is ParenthesizedLambdaExpressionSyntax paren && paren.ParameterList.Parameters.All(p => p.Type is not null)
        ? paren.ParameterList.Parameters.ToString()
        : null;
}
