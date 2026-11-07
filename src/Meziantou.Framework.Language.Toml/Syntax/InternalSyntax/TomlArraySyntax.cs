using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml.Syntax.InternalSyntax;

/// <summary>An array: brackets around a comma-separated list of values.</summary>
internal sealed class TomlArraySyntax : TomlValueSyntax
{
    private readonly GreenNode _openBracketToken;
    private readonly GreenNode? _elements;
    private readonly GreenNode _closeBracketToken;

    public TomlArraySyntax(GreenNode openBracketToken, GreenNode? elements, GreenNode closeBracketToken)
        : this(openBracketToken, elements, closeBracketToken, diagnostics: null, annotations: null)
    {
    }

    private TomlArraySyntax(GreenNode openBracketToken, GreenNode? elements, GreenNode closeBracketToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.TomlArray, diagnostics, annotations)
    {
        SlotCount = 3;
        AdjustFlagsAndWidth(openBracketToken);
        _openBracketToken = openBracketToken;
        AdjustFlagsAndWidth(elements);
        _elements = elements;
        AdjustFlagsAndWidth(closeBracketToken);
        _closeBracketToken = closeBracketToken;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _openBracketToken,
        1 => _elements,
        2 => _closeBracketToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new TomlArraySyntax(RequiredSlot(slots[0]), slots[1], RequiredSlot(slots[2]), GetDiagnostics(), GetAnnotations());
    internal override bool IsSeparatedListSlot(int index) => index is 1;
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new TomlArraySyntax(_openBracketToken, _elements, _closeBracketToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new TomlArraySyntax(_openBracketToken, _elements, _closeBracketToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Toml.TomlArraySyntax(this, parent, position);
}
