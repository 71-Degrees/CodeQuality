/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeQuality.Refactor.Fixers;

/// <summary>CA1816: a <c>Dispose()</c> must call <c>GC.SuppressFinalize(this)</c>, so a subclass with a finalizer is not finalized after it has already been disposed.</summary>
internal sealed class SuppressFinalizeFixer : IFixer
{
    private const string Statement = "GC.SuppressFinalize(this);";

    public IReadOnlyCollection<string> Rules { get; } = new[] { "CA1816" };

    public string? RequiredUsing => null;

    public string? Fix(string text, int offset, Warning warning)
    {
        var method = SourceText.MethodAt(SourceText.Parse(text), offset);
        if (method is null || method.ToString().Contains("SuppressFinalize", StringComparison.Ordinal))
        {
            return null;
        }

        if (method.Body is not null)
        {
            return SourceText.InsertBeforeClosingBrace(text, method.Body.CloseBraceToken, (indent, newline) => indent + Statement + newline);
        }

        return method.ExpressionBody is null ? null : ConvertToBlock(text, method);
    }

    /// <summary><c>public void Dispose() =&gt; _lock.Dispose();</c> becomes a block body holding that statement and the SuppressFinalize call.</summary>
    private static string ConvertToBlock(string text, MethodDeclarationSyntax method)
    {
        var newline = SourceText.NewLine(text);
        var lineStart = text.LastIndexOf('\n', method.SpanStart) + 1;
        var indent = text[lineStart..method.SpanStart];
        indent = new string(' ', indent.Length - indent.TrimStart().Length);
        var from = method.ParameterList.Span.End;
        var to = method.SemicolonToken.Span.End;
        var block = $"{newline}{indent}{{{newline}{indent}    {method.ExpressionBody!.Expression};{newline}{indent}    {Statement}{newline}{indent}}}";
        return text[..from] + block + text[to..];
    }
}
