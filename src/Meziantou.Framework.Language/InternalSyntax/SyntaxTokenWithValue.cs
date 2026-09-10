namespace Meziantou.Framework.Language.InternalSyntax;

/// <summary>A token whose text and meaning differ, such as a quoted string or a number.</summary>
/// <typeparam name="TValue">The type the text decodes to.</typeparam>
internal sealed class SyntaxTokenWithValue<TValue> : SyntaxToken
{
    private readonly TValue _value;

    internal SyntaxTokenWithValue(int rawKind, string text, TValue value, string valueText, GreenNode? leadingTrivia, GreenNode? trailingTrivia, bool isMissing)
        : base(rawKind, text, leadingTrivia, trailingTrivia, isMissing)
    {
        _value = value;
        ValueText = valueText;
    }

    private SyntaxTokenWithValue(int rawKind, string text, TValue value, string valueText, GreenNode? leadingTrivia, GreenNode? trailingTrivia, bool isMissing, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(rawKind, text, leadingTrivia, trailingTrivia, isMissing, diagnostics, annotations)
    {
        _value = value;
        ValueText = valueText;
    }

    public override string ValueText { get; }

    internal override object? GetValue() => _value;

    internal override SyntaxToken WithTrivia(GreenNode? leadingTrivia, GreenNode? trailingTrivia)
        => new SyntaxTokenWithValue<TValue>(RawKind, Text, _value, ValueText, leadingTrivia, trailingTrivia, IsMissing, GetDiagnostics(), GetAnnotations());

    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics)
        => new SyntaxTokenWithValue<TValue>(RawKind, Text, _value, ValueText, LeadingTrivia, TrailingTrivia, IsMissing, diagnostics, GetAnnotations());

    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations)
        => new SyntaxTokenWithValue<TValue>(RawKind, Text, _value, ValueText, LeadingTrivia, TrailingTrivia, IsMissing, GetDiagnostics(), annotations);
}
