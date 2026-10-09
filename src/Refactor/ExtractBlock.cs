/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using System.Text;
using System.Text.RegularExpressions;

namespace CodeQuality.Refactor;

/// <summary>What to move: lines <see cref="From"/> to <see cref="To"/> of a source file become the body of a static extension method in another file.</summary>
internal sealed record ExtractRequest(
    int From,
    int To,
    string Class,
    string Method,
    string Receiver,
    string Parameters,
    string Namespace,
    string Summary,
    string? Call = null);

/// <summary>The two files after the move: the source with a call in place of the block, and the target holding the method.</summary>
internal sealed record ExtractPlan(string Source, string Target, int MovedLines);

/// <summary>
/// Moves a block of top-level statements (typically a group of endpoint mappings in Program.cs) into a static extension method in its own file, leaving a single call behind. It works on text
/// and does no analysis: the caller names the parameters the block needs, and the compiler says which are missing. What it does guarantee is that the block itself is carried over unchanged -
/// only indented one level, and never inside the text of a multi-line string.
/// </summary>
internal static partial class ExtractBlock
{
    [GeneratedRegex(@"^using [^;]+;\s*$", RegexOptions.Multiline)]
    private static partial Regex UsingLine();

    /// <param name="source">The file the block is taken from.</param>
    /// <param name="existingTarget">The target file's current text when it already exists (the method is added to its class), else null.</param>
    public static ExtractPlan Plan(string source, string? existingTarget, ExtractRequest request)
    {
        var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        ValidateRange(lines, request);

        var block = string.Join('\n', lines[(request.From - 1)..request.To]);
        var method = MethodText(block, request);
        var target = existingTarget is null ? NewFile(source, request, method) : AppendMethod(existingTarget.Replace("\r\n", "\n", StringComparison.Ordinal), method);
        var call = request.Call ?? DefaultCall(request);
        var rest = lines[..(request.From - 1)].Append(call).Concat(lines[request.To..]);
        return new ExtractPlan(string.Join(newline, rest), target.Replace("\n", newline, StringComparison.Ordinal), request.To - request.From + 1);
    }

    private static void ValidateRange(string[] lines, ExtractRequest request)
    {
        if (request.From < 1 || request.To < request.From || request.To > lines.Length)
        {
            throw new ArgumentException($"Lines {request.From}-{request.To} are not inside a file of {lines.Length} lines.");
        }
    }

    private static string MethodText(string block, ExtractRequest request)
    {
        var parameters = request.Parameters.Trim().Length == 0 ? string.Empty : ", " + request.Parameters.Trim();
        var summary = request.Summary.Trim().Length == 0 ? string.Empty : $"    /// <summary>{request.Summary.Trim()}</summary>\n";
        return $"{summary}    public static void {request.Method}(this {request.Receiver}{parameters})\n    {{\n{Indenter.Indent(block, 8)}\n    }}\n";
    }

    private static string NewFile(string source, ExtractRequest request, string method)
    {
        var text = new StringBuilder();
        text.Append(Header(source)).Append('\n');
        foreach (Match match in UsingLine().Matches(source.Replace("\r\n", "\n", StringComparison.Ordinal)))
        {
            text.Append(match.Value.TrimEnd()).Append('\n');
        }

        text.Append($"\nnamespace {request.Namespace};\n\n");
        text.Append($"internal static class {request.Class}\n{{\n{method}}}\n");
        return text.ToString();
    }

    /// <summary>The copyright comment at the top of the source file, so every file starts the same way.</summary>
    private static string Header(string source)
    {
        var text = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var end = text.IndexOf("*/", StringComparison.Ordinal);
        return text.StartsWith("/*", StringComparison.Ordinal) && end > 0 ? text[..(end + 2)] + "\n" : string.Empty;
    }

    private static string AppendMethod(string target, string method)
    {
        var close = target.LastIndexOf('}');
        if (close < 0)
        {
            throw new ArgumentException("The target file has no class to add the method to.");
        }

        return target[..close].TrimEnd() + "\n\n" + method + "}\n";
    }

    private static string DefaultCall(ExtractRequest request)
    {
        var receiverName = ParameterList.Names(request.Receiver)[0];
        var names = ParameterList.Names(request.Parameters);
        return $"{receiverName}.{request.Method}({string.Join(", ", names)});";
    }
}
