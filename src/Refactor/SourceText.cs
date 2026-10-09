/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeQuality.Refactor;

/// <summary>Finding things in source text: where a warning points, which node it means, and the text edits fixers make.</summary>
internal static partial class SourceText
{
    [GeneratedRegex(@"'(?:[\w<>?\[\]]+\.)*(?<name>\w+)\(")]
    private static partial Regex QuotedMethod();

    [GeneratedRegex(@"field\(s\) '(?<names>[^']+)'")]
    private static partial Regex OwnedFields();

    /// <summary>A 1-based line and column to a zero-based offset in the text.</summary>
    public static int Offset(string text, int line, int column)
    {
        var offset = 0;
        for (var current = 1; current < line; current++)
        {
            var next = text.IndexOf('\n', offset);
            if (next < 0)
            {
                return -1;
            }

            offset = next + 1;
        }

        return offset + column - 1;
    }

    public static SyntaxNode Parse(string text) => CSharpSyntaxTree.ParseText(text).GetRoot();

    public static string NewLine(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    /// <summary>The type declaration whose name sits at the offset.</summary>
    public static TypeDeclarationSyntax? TypeAt(SyntaxNode root, int offset) =>
        root.FindToken(offset).Parent?.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();

    /// <summary>The method whose name sits at the offset.</summary>
    public static MethodDeclarationSyntax? MethodAt(SyntaxNode root, int offset) =>
        root.FindToken(offset).Parent?.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();

    /// <summary>The method name an analyzer message talks about: <c>'int.Parse(string)'</c> gives <c>Parse</c>.</summary>
    public static string? MethodNameIn(string message)
    {
        var match = QuotedMethod().Match(message);
        return match.Success ? match.Groups["name"].Value : null;
    }

    /// <summary>The fields an "owns disposable field(s) '_a, _b'" message names.</summary>
    public static IReadOnlyList<string> OwnedFieldNames(string message)
    {
        var match = OwnedFields().Match(message);
        return match.Success ? match.Groups["names"].Value.Split(", ", StringSplitOptions.RemoveEmptyEntries) : Array.Empty<string>();
    }

    /// <summary>The call the warning at <paramref name="offset"/> is about: an invocation that starts there, or whose <c>.Name</c> starts there, and that has the named method.</summary>
    public static InvocationExpressionSyntax? CallAt(SyntaxNode root, int offset, string method) =>
        root.DescendantNodes().OfType<InvocationExpressionSyntax>().FirstOrDefault(call => NameOf(call) == method && StartsAt(call, offset));

    /// <summary>The compiler points at the start of the call, at the dot before its name, or - for <c>a?.M()</c> - at the start of the whole conditional access.</summary>
    private static bool StartsAt(InvocationExpressionSyntax call, int offset) =>
        call.SpanStart == offset
        || (call.Expression is MemberAccessExpressionSyntax access && access.OperatorToken.SpanStart == offset)
        || call.Ancestors().OfType<ConditionalAccessExpressionSyntax>().Any(conditional => conditional.SpanStart == offset);

    public static string? NameOf(InvocationExpressionSyntax call) => call.Expression switch
    {
        MemberAccessExpressionSyntax access => access.Name.Identifier.Text,
        MemberBindingExpressionSyntax binding => binding.Name.Identifier.Text,
        IdentifierNameSyntax name => name.Identifier.Text,
        _ => null,
    };

    // ---- edits (each returns the new text) ----

    /// <summary>Adds an argument as the first one: <c>Format(a, b)</c> becomes <c>Format(X, a, b)</c>.</summary>
    public static string InsertFirstArgument(string text, InvocationExpressionSyntax call, string argument)
    {
        var arguments = call.ArgumentList;
        return text.Insert(arguments.OpenParenToken.Span.End, arguments.Arguments.Count == 0 ? argument : argument + ", ");
    }

    /// <summary>Adds an argument as the last one: <c>Parse(s)</c> becomes <c>Parse(s, X)</c>.</summary>
    public static string AppendArgument(string text, InvocationExpressionSyntax call, string argument)
    {
        var arguments = call.ArgumentList;
        return arguments.Arguments.Count == 0
            ? text.Insert(arguments.OpenParenToken.Span.End, argument)
            : text.Insert(arguments.Arguments[^1].Span.End, ", " + argument);
    }

    /// <summary>Adds an argument before the last one: <c>TryParse(s, out v)</c> becomes <c>TryParse(s, X, out v)</c>.</summary>
    public static string InsertBeforeLastArgument(string text, InvocationExpressionSyntax call, string argument) =>
        call.ArgumentList.Arguments.Count == 0 ? text : text.Insert(call.ArgumentList.Arguments[^1].SpanStart, argument + ", ");

    /// <summary>Puts a block of lines just before the line holding a closing brace, indented one level deeper than the brace. When the brace shares its line with code, the text goes inline before it.</summary>
    public static string InsertBeforeClosingBrace(string text, SyntaxToken closeBrace, Func<string, string, string> lines)
    {
        var close = closeBrace.SpanStart;
        var lineStart = text.LastIndexOf('\n', Math.Max(0, close - 1)) + 1;
        var indent = text[lineStart..close];
        var newline = NewLine(text);
        if (!string.IsNullOrWhiteSpace(indent))
        {
            return text.Insert(close, lines(string.Empty, " ").Trim() + " ");
        }

        return text.Insert(lineStart, lines(indent + "    ", newline));
    }
}
