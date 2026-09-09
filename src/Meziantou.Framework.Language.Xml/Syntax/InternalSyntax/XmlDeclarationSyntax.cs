using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

/// <summary>The XML declaration, whose version, encoding and standalone are written as attributes.</summary>
internal sealed class XmlDeclarationSyntax : XmlNodeSyntax
{
    private readonly GreenNode _startDeclarationToken;
    private readonly GreenNode? _attributes;
    private readonly GreenNode _endDeclarationToken;

    public XmlDeclarationSyntax(GreenNode startDeclarationToken, GreenNode? attributes, GreenNode endDeclarationToken)
        : this(startDeclarationToken, attributes, endDeclarationToken, diagnostics: null, annotations: null)
    {
    }

    private XmlDeclarationSyntax(GreenNode startDeclarationToken, GreenNode? attributes, GreenNode endDeclarationToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.XmlDeclaration, diagnostics, annotations)
    {
        SlotCount = 3;
        AdjustFlagsAndWidth(startDeclarationToken);
        _startDeclarationToken = startDeclarationToken;
        AdjustFlagsAndWidth(attributes);
        _attributes = attributes;
        AdjustFlagsAndWidth(endDeclarationToken);
        _endDeclarationToken = endDeclarationToken;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _startDeclarationToken,
        1 => _attributes,
        2 => _endDeclarationToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new XmlDeclarationSyntax(RequiredSlot(slots[0]), slots[1], RequiredSlot(slots[2]), GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new XmlDeclarationSyntax(_startDeclarationToken, _attributes, _endDeclarationToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new XmlDeclarationSyntax(_startDeclarationToken, _attributes, _endDeclarationToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Xml.XmlDeclarationSyntax(this, parent, position);
}
