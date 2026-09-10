using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

/// <summary>The opening tag of an element, with the attributes it declares.</summary>
internal sealed class XmlElementStartTagSyntax : XmlSyntaxNode
{
    private readonly GreenNode _lessThanToken;
    private readonly GreenNode _nameToken;
    private readonly GreenNode? _attributes;
    private readonly GreenNode _greaterThanToken;

    public XmlElementStartTagSyntax(GreenNode lessThanToken, GreenNode nameToken, GreenNode? attributes, GreenNode greaterThanToken)
        : this(lessThanToken, nameToken, attributes, greaterThanToken, diagnostics: null, annotations: null)
    {
    }

    private XmlElementStartTagSyntax(GreenNode lessThanToken, GreenNode nameToken, GreenNode? attributes, GreenNode greaterThanToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.XmlElementStartTag, diagnostics, annotations)
    {
        SlotCount = 4;
        AdjustFlagsAndWidth(lessThanToken);
        _lessThanToken = lessThanToken;
        AdjustFlagsAndWidth(nameToken);
        _nameToken = nameToken;
        AdjustFlagsAndWidth(attributes);
        _attributes = attributes;
        AdjustFlagsAndWidth(greaterThanToken);
        _greaterThanToken = greaterThanToken;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _lessThanToken,
        1 => _nameToken,
        2 => _attributes,
        3 => _greaterThanToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new XmlElementStartTagSyntax(RequiredSlot(slots[0]), RequiredSlot(slots[1]), slots[2], RequiredSlot(slots[3]), GetDiagnostics(), GetAnnotations());

    internal override bool IsListSlot(int index) => index is 2;
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new XmlElementStartTagSyntax(_lessThanToken, _nameToken, _attributes, _greaterThanToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new XmlElementStartTagSyntax(_lessThanToken, _nameToken, _attributes, _greaterThanToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Xml.XmlElementStartTagSyntax(this, parent, position);
}
