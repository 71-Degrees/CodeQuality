/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeQuality.Refactor;

/// <summary>One lambda that became a method: its new name, the route it serves, and the locals of the registering method it still reaches for (which a person must give a home).</summary>
internal sealed record ConvertedHandler(string Name, string Route, IReadOnlyList<string> Captures);

internal sealed record HandlerConversion(string Text, IReadOnlyList<ConvertedHandler> Converted, IReadOnlyList<string> Skipped);

/// <summary>
/// Turns the inline lambda of each <c>app.MapPost("/route", async (...) =&gt; { ... })</c> into a named method, so the registering method becomes a plain list of routes and each handler can be read,
/// measured and tested on its own. The body is carried over unchanged (moved out one indent). A handler that uses variables of the registering method is converted all the same and reported, because
/// those variables need a proper home (a field, a setting, a service) that only a person can choose.
/// </summary>
internal static class LambdaHandlers
{
    public static HandlerConversion Convert(string source, bool instance)
    {
        var newline = SourceText.NewLine(source);
        var text = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var root = SourceText.Parse(text);
        var taken = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Select(m => m.Identifier.Text).ToHashSet(StringComparer.Ordinal);
        var converted = new List<ConvertedHandler>();
        var skipped = new List<string>();
        var edits = new List<TextEdit>();
        var appended = new Dictionary<MethodDeclarationSyntax, List<string>>();

        foreach (var handler in HandlerFinder.Find(root))
        {
            if (!CanConvert(handler))
            {
                skipped.Add($"{handler.MapMethod} \"{handler.Route}\" (parameter types are inferred, or it is an async expression body)");
                continue;
            }

            var name = Unique(RouteNamer.NameFor(handler.MapMethod, handler.Route, handler.IsAsync), taken);
            edits.Add(new TextEdit(handler.Lambda.SpanStart, handler.Lambda.Span.Length, name));
            appended.TryAdd(handler.Container, new List<string>());
            appended[handler.Container].Add(MethodText(text, handler, name, instance));
            converted.Add(new ConvertedHandler(name, $"{handler.MapMethod} {handler.Route}", Captures.Of(handler)));
        }

        edits.AddRange(appended.Select(a => new TextEdit(a.Key.Span.End, 0, "\n\n" + string.Join("\n\n", a.Value))));
        return new HandlerConversion(Apply(text, edits).Replace("\n", newline, StringComparison.Ordinal), converted, skipped);
    }

    private static bool CanConvert(Handler handler) =>
        handler.ParameterText is not null && !(handler.IsAsync && handler.Lambda.ExpressionBody is not null);

    private static string Unique(string name, HashSet<string> taken)
    {
        var candidate = name;
        for (var i = 2; !taken.Add(candidate); i++)
        {
            candidate = name + i;
        }

        return candidate;
    }

    private static string MethodText(string text, Handler handler, string name, bool instance)
    {
        var returnType = handler.IsAsync ? "async Task<IResult>" : "IResult";
        var signature = $"    private {(instance ? string.Empty : "static ")}{returnType} {name}({ParametersText(handler)})";
        if (handler.Lambda.Block is { } block)
        {
            return signature + "\n    " + Indenter.Dedent(text[block.SpanStart..block.Span.End], 4);
        }

        var expression = text[handler.Lambda.ExpressionBody!.SpanStart..handler.Lambda.ExpressionBody.Span.End];
        return $"{signature} =>\n        {expression};";
    }

    /// <summary>The parameters on one line when they fit, else one to a line.</summary>
    private static string ParametersText(Handler handler)
    {
        var parameters = ((ParenthesizedLambdaExpressionSyntax)handler.Lambda).ParameterList.Parameters.Select(p => p.ToString()).ToList();
        var oneLine = string.Join(", ", parameters);
        return oneLine.Length <= 110 ? oneLine : "\n        " + string.Join(",\n        ", parameters);
    }

    /// <summary>Applies the edits from the end of the text to the start, so no edit moves the position of one still to be made.</summary>
    private static string Apply(string text, IEnumerable<TextEdit> edits)
    {
        var builder = new StringBuilder(text);
        foreach (var edit in edits.OrderByDescending(e => e.Position))
        {
            builder.Remove(edit.Position, edit.Length).Insert(edit.Position, edit.Replacement);
        }

        return builder.ToString();
    }

    private readonly record struct TextEdit(int Position, int Length, string Replacement);
}
