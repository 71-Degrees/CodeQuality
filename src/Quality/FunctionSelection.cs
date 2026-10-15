/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text.RegularExpressions;
using CodeQuality.Common;

namespace CodeQuality.Quality;

/// <summary>
/// Which functions of the site a change touches, so only their tests need to run: a changed page or component stands for the routes it draws, a changed C# file for the addresses it names ("/account/login"), and the
/// site's own list of functions (<c>quality/features.json</c>) says which function owns each address and which test classes are about it. A change that touches no sign-in code runs no sign-in tests; a test that merely
/// passes through the sign-in page on its way to somewhere else is not about it and is not selected for it.
/// </summary>
internal static partial class FunctionSelection
{
    /// <summary>Addresses shorter than this ("/", "/a") would match nearly anything.</summary>
    private const int MinRouteLength = 3;

    [GeneratedRegex("\"(?<route>/[A-Za-z0-9][A-Za-z0-9/_-]*)")]
    private static partial Regex RouteLiteral();

    /// <summary>The addresses a C# file names in string literals ("/wave/strands/update"): what an endpoint file maps, or what a handler redirects to.</summary>
    internal static IReadOnlyList<string> RoutesNamed(string csharp) =>
        RouteLiteral().Matches(csharp).Select(m => m.Groups["route"].Value.TrimEnd('/')).Where(r => r.Length >= MinRouteLength).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// The functions that own these addresses. An address belongs to the function whose own address is the longest start of it ("/wave/strands/update" to the one for "/wave/strands", not the one for "/wave");
    /// the home page ("/") only ever matches itself. A function's address with a parameter ("/chronicle/{Slug}") counts up to the brace.
    /// </summary>
    internal static IReadOnlyList<FeatureFunction> Owning(FeatureRegistryFile registry, IEnumerable<string> addresses)
    {
        var owned = new List<FeatureFunction>();
        foreach (var address in addresses)
        {
            if (address == "/")
            {
                owned.AddRange(registry.Functions.Where(f => f.Routes.Contains("/")));
                continue;
            }

            var candidates = registry.Functions
                .SelectMany(f => f.Routes.Select(route => (Function: f, Base: BaseOf(route))))
                .Where(c => c.Base.Length >= MinRouteLength && (address.Equals(c.Base, StringComparison.OrdinalIgnoreCase) || address.StartsWith(c.Base + "/", StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (candidates.Count == 0)
            {
                continue;
            }

            var longest = candidates.Max(c => c.Base.Length);
            owned.AddRange(candidates.Where(c => c.Base.Length == longest).Select(c => c.Function));
        }

        return owned.DistinctBy(f => f.Id).ToList();
    }

    /// <summary>"/chronicle/{Slug}/map" is "/chronicle": the fixed start of a route.</summary>
    private static string BaseOf(string route)
    {
        var brace = route.IndexOf('{', StringComparison.Ordinal);
        return (brace < 0 ? route : route[..brace]).TrimEnd('/');
    }
}
