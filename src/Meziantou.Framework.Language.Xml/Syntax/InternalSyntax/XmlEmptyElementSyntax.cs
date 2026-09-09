using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

/// <summary>An element written as one self-closing tag.</summary>
internal sealed class XmlEmptyElementSyntax : XmlNodeSyntax
{
    private readonly GreenNode _lessThanToken;
    private readonly GreenNode _nameToken;
    private readonly GreenNode? _attributes;
    private readonly GreenNode _slashGreaterThanToken;

    public XmlEmptyElementSyntax(GreenNode lessThanToken, GreenNode nameToken, GreenNode? attributes, GreenNode slashGreaterThanToken)
        : this(lessThanToken, nameToken, attributes, slashGreaterThanToken, diagnostics: null, annotations: null)
    {
    }

    private XmlEmptyElementSyntax(GreenNode lessThanToken, GreenNode nameToken, GreenNode? attributes, GreenNode slashGreaterThanToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.XmlEmptyElement, diagnostics, annotations)
    {
        SlotCount = 4;
        AdjustFlagsAndWidth(lessThanToken);
        _lessThanToken = lessThanToken;
        AdjustFlagsAndWidth(nameToken);
        _nameToken = nameToken;
        AdjustFlagsAndWidth(attributes);
        _attributes = attributes;
        AdjustFlagsAndWidth(slashGreaterThanToken);
        _slashGreaterThanToken = slashGreaterThanToken;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _lessThanToken,
        1 => _nameToken,
        2 => _attributes,
        3 => _slashGreaterThanToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new XmlEmptyElementSyntax(RequiredSlot(slots[0]), RequiredSlot(slots[1]), slots[2], RequiredSlot(slots[3]), GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new XmlEmptyElementSyntax(_lessThanToken, _nameToken, _attributes, _slashGreaterThanToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new XmlEmptyElementSyntax(_lessThanToken, _nameToken, _attributes, _slashGreaterThanToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Xml.XmlEmptyElementSyntax(this, parent, position);
}
