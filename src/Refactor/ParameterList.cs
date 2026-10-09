/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

namespace CodeQuality.Refactor;

/// <summary>Reads a parameter list written as text (<c>"string a, Func&lt;int, bool&gt; b"</c>) far enough to know the parameter names.</summary>
internal static class ParameterList
{
    private static readonly System.Buffers.SearchValues<char> NameBoundary = System.Buffers.SearchValues.Create(" >)]");

    /// <summary>The names, in order. Commas inside generic brackets, parentheses or brackets do not separate parameters.</summary>
    public static IReadOnlyList<string> Names(string parameters) =>
        Split(parameters).Select(NameOf).ToList();

    private static IEnumerable<string> Split(string parameters)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            depth += parameters[i] switch { '<' or '(' or '[' => 1, '>' or ')' or ']' => -1, _ => 0 };
            if (parameters[i] == ',' && depth == 0)
            {
                yield return parameters[start..i];
                start = i + 1;
            }
        }

        if (parameters[start..].Trim().Length > 0)
        {
            yield return parameters[start..];
        }
    }

    /// <summary>The last word of one parameter, ignoring a default value (<c>int n = 3</c> gives <c>n</c>).</summary>
    private static string NameOf(string parameter)
    {
        var withoutDefault = parameter.Split('=')[0].Trim();
        return withoutDefault[(withoutDefault.AsSpan().LastIndexOfAny(NameBoundary) + 1)..];
    }
}
