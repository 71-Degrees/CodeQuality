/*
 * ---------------------------------------------------------------------------
 * Copyright © 2026 71 Degrees. All rights reserved.
 * ---------------------------------------------------------------------------
 */

using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeQuality.Refactor.Fixers;

/// <summary>CA1001: a class that owns something disposable (a lock, a stream) must be disposable itself, or nothing ever releases it. The fix makes the class implement <c>IDisposable</c> and dispose the fields it owns.</summary>
internal sealed class DisposableOwnerFixer : IFixer
{
    public IReadOnlyCollection<string> Rules { get; } = new[] { "CA1001" };

    public string? RequiredUsing => null;

    public string? Fix(string text, int offset, Warning warning)
    {
        var type = SourceText.TypeAt(SourceText.Parse(text), offset);
        var fields = SourceText.OwnedFieldNames(warning.Message);
        if (type is null || type is RecordDeclarationSyntax || fields.Count == 0 || HasMethodNamedDispose(type))
        {
            return null;
        }

        // The later edit first (the method before the closing brace), so the earlier position (the base list) is still where it was.
        var withMethod = SourceText.InsertBeforeClosingBrace(text, type.CloseBraceToken, (indent, newline) => DisposeMethod(type, fields, indent, newline));
        return withMethod.Insert(BaseListPosition(type), type.BaseList is null ? " : IDisposable" : ", IDisposable");
    }

    private static bool HasMethodNamedDispose(TypeDeclarationSyntax type) =>
        type.Members.OfType<MethodDeclarationSyntax>().Any(m => m.Identifier.Text == "Dispose");

    private static int BaseListPosition(TypeDeclarationSyntax type) =>
        type.BaseList is not null ? type.BaseList.Types[^1].Span.End : (type.ParameterList?.Span.End ?? type.TypeParameterList?.Span.End ?? type.Identifier.Span.End);

    private static string DisposeMethod(TypeDeclarationSyntax type, IReadOnlyList<string> fields, string indent, string newline)
    {
        var body = string.Concat(fields.Select(f => $"{indent}    {f}{(IsNullable(type, f) ? "?" : string.Empty)}.Dispose();{newline}"));
        return newline +
            $"{indent}/// <summary>Releases the disposable fields this type owns.</summary>{newline}" +
            $"{indent}public void Dispose(){newline}{indent}{{{newline}{body}{indent}    GC.SuppressFinalize(this);{newline}{indent}}}{newline}";
    }

    private static bool IsNullable(TypeDeclarationSyntax type, string field) =>
        type.Members.OfType<FieldDeclarationSyntax>().Any(f => f.Declaration.Type is NullableTypeSyntax && f.Declaration.Variables.Any(v => v.Identifier.Text == field));
}
