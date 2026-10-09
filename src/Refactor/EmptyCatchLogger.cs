/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeQuality.Refactor;

/// <summary>What <see cref="EmptyCatchLogger.Apply"/> did to one file.</summary>
internal sealed record EmptyCatchResult(string Text, IReadOnlyList<string> Logged, IReadOnlyList<string> Skipped);

/// <summary>
/// The owner's error rule says no empty <c>catch</c>: at minimum a log line saying what was ignored. This adds that line to every empty catch in a type that already has an <c>ILogger</c>, saying which
/// exception was ignored where. A cancellation is logged without the exception (it is how a service stops, not a fault); anything else is logged with it, naming the variable if the catch has none.
/// A catch in a type with no logger, or in a static method that would need an instance logger, is left for a person and reported.
/// </summary>
internal static class EmptyCatchLogger
{
    private static readonly string[] LoggerNames = { "_logger", "_log", "logger", "log" };

    public static EmptyCatchResult Apply(string text)
    {
        var root = SourceText.Parse(text);
        var logged = new List<string>();
        var skipped = new List<string>();
        var plans = new List<(CatchClauseSyntax Catch, string Logger, string Where)>();

        foreach (var clause in root.DescendantNodes().OfType<CatchClauseSyntax>().Where(IsEmpty))
        {
            var where = Describe(clause);
            if (LoggerFor(clause) is { } logger)
            {
                plans.Add((clause, logger, where));
            }
            else
            {
                skipped.Add(where);
            }
        }

        // Edit from the bottom up, so each edit leaves the offsets of the ones still to come untouched.
        foreach (var plan in plans.OrderByDescending(p => p.Catch.SpanStart))
        {
            text = Edit(text, plan.Catch, plan.Logger, plan.Where);
            logged.Add(plan.Where);
        }

        logged.Reverse();
        return new EmptyCatchResult(text, logged, skipped);
    }

    private static bool IsEmpty(CatchClauseSyntax clause) => clause.Block.Statements.Count == 0;

    private static string Edit(string text, CatchClauseSyntax clause, string logger, string where)
    {
        var typeName = clause.Declaration?.Type.ToString() ?? "Exception";
        var isCancellation = typeName is "OperationCanceledException" or "TaskCanceledException";
        var variable = clause.Declaration?.Identifier.Text;
        var needsVariable = !isCancellation && string.IsNullOrEmpty(variable);
        var name = needsVariable ? "ex" : variable;
        var statement = isCancellation
            ? $"{logger}.LogDebug(\"{where} stopped: cancelled\");"
            : $"{logger}.LogDebug({name}, \"{where} ignored a {typeName}\");";

        // Do the later edit first (the body), then the earlier one (the declaration), so offsets hold.
        text = SourceText.InsertBeforeClosingBrace(text, clause.Block.CloseBraceToken, (indent, newline) => indent + statement + newline);
        return needsVariable ? AddVariable(text, clause, name!) : text;
    }

    private static string AddVariable(string text, CatchClauseSyntax clause, string name) =>
        clause.Declaration is { } declaration
            ? text.Insert(declaration.Type.Span.End, " " + name)
            : text.Insert(clause.CatchKeyword.Span.End, " (Exception " + name + ")");

    /// <summary>"Class.Method" for the message; the type and member the catch sits in.</summary>
    private static string Describe(CatchClauseSyntax clause)
    {
        var type = clause.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()?.Identifier.Text ?? "?";
        var member = clause.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault() switch
        {
            MethodDeclarationSyntax method => method.Identifier.Text,
            ConstructorDeclarationSyntax => type,
            _ => clause.Ancestors().OfType<LocalFunctionStatementSyntax>().FirstOrDefault()?.Identifier.Text ?? "member",
        };
        return $"{type}.{member}";
    }

    /// <summary>The logger the enclosing type holds - a field or a primary-constructor parameter of type <c>ILogger</c>/<c>ILogger&lt;T&gt;</c> - when the catch can reach it.</summary>
    private static string? LoggerFor(CatchClauseSyntax clause)
    {
        if (clause.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault() is not { } type)
        {
            return null;
        }

        var inStatic = clause.Ancestors().OfType<MemberDeclarationSyntax>().FirstOrDefault() is MethodDeclarationSyntax method && method.Modifiers.Any(SyntaxKind.StaticKeyword);
        var candidates = type.Members.OfType<FieldDeclarationSyntax>()
            .Where(f => IsLoggerType(f.Declaration.Type) && (!inStatic || f.Modifiers.Any(SyntaxKind.StaticKeyword)))
            .SelectMany(f => f.Declaration.Variables.Select(v => v.Identifier.Text))
            .Concat((type.ParameterList?.Parameters ?? default).Where(p => p.Type is not null && IsLoggerType(p.Type) && !inStatic).Select(p => p.Identifier.Text))
            .ToList();

        return LoggerNames.FirstOrDefault(candidates.Contains) ?? candidates.FirstOrDefault();
    }

    private static bool IsLoggerType(TypeSyntax type) =>
        type.ToString() is "ILogger" or "Microsoft.Extensions.Logging.ILogger" || type.ToString().StartsWith("ILogger<", StringComparison.Ordinal);
}
