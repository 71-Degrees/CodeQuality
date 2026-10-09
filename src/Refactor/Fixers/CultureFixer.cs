/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeQuality.Refactor.Fixers;

/// <summary>
/// CA1305: formatting and parsing that silently depends on the machine's locale (a German server would print 1,5 and refuse 1.5). The fix names the culture: invariant, which is what a stored or
/// transmitted value needs and what keeps tests the same on every machine.
/// </summary>
internal sealed class CultureFixer : IFixer
{
    private const string Provider = "CultureInfo.InvariantCulture";

    public IReadOnlyCollection<string> Rules { get; } = new[] { "CA1305" };

    public string? RequiredUsing => "System.Globalization";

    public string? Fix(string text, int offset, Warning warning)
    {
        var method = SourceText.MethodNameIn(warning.Message);
        var call = method is null ? null : SourceText.CallAt(SourceText.Parse(text), offset, method);
        return call is null ? null : Apply(text, call, method!);
    }

    private static string? Apply(string text, InvocationExpressionSyntax call, string method) => method switch
    {
        "Format" => SourceText.InsertFirstArgument(text, call, Provider),
        "Append" or "AppendLine" => IsInterpolated(call) ? SourceText.InsertFirstArgument(text, call, Provider) : null,
        "TryParse" => SourceText.InsertBeforeLastArgument(text, call, Provider),
        "Parse" or "ToString" or "RenderMessage" => SourceText.AppendArgument(text, call, Provider),
        _ when method.StartsWith("To", StringComparison.Ordinal) => SourceText.AppendArgument(text, call, Provider),   // Convert.ToInt32(s) and friends
        _ => null,
    };

    /// <summary>StringBuilder.Append($"...") has an overload that takes the provider first; plain Append(string) is not culture-dependent and is never flagged.</summary>
    private static bool IsInterpolated(InvocationExpressionSyntax call) =>
        call.ArgumentList.Arguments.FirstOrDefault()?.Expression is InterpolatedStringExpressionSyntax;
}
