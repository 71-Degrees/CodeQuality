/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Globalization;
using System.Text;

namespace CodeQuality.Refactor;

/// <summary>Names a handler method after the route it serves: <c>MapPost "/api/quiz-requests/{id}/claim"</c> becomes <c>PostQuizRequestsByIdClaim</c>.</summary>
internal static class RouteNamer
{
    private static readonly string[] Ignored = { "api" };

    /// <param name="mapMethod">The registering call, e.g. <c>MapPost</c>.</param>
    public static string NameFor(string mapMethod, string route, bool isAsync)
    {
        var verb = mapMethod.StartsWith("Map", StringComparison.Ordinal) ? mapMethod[3..] : mapMethod;
        var name = new StringBuilder(verb);
        foreach (var segment in route.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(s => !Ignored.Contains(s)))
        {
            name.Append(Pascal(segment));
        }

        var text = name.ToString();
        return text.Length > verb.Length ? text + (isAsync ? "Async" : string.Empty) : verb + "Root" + (isAsync ? "Async" : string.Empty);
    }

    /// <summary><c>quiz-requests</c> becomes <c>QuizRequests</c>; a route parameter <c>{id}</c> becomes <c>ById</c>; punctuation and file extensions are dropped.</summary>
    private static string Pascal(string segment)
    {
        var parameter = segment.StartsWith('{');
        var words = new string(segment.Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var joined = string.Concat(words.Select(w => char.ToUpper(w[0], CultureInfo.InvariantCulture) + w[1..]));
        return parameter ? "By" + joined : joined;
    }
}
