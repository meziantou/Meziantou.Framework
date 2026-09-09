using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

/// <summary>A document type declaration.</summary>
internal sealed class XmlDocumentTypeSyntax : XmlNodeSyntax
{
    private readonly GreenNode _startDocumentTypeToken;
    private readonly GreenNode _nameToken;
    private readonly GreenNode _contentToken;
    private readonly GreenNode _greaterThanToken;

    public XmlDocumentTypeSyntax(GreenNode startDocumentTypeToken, GreenNode nameToken, GreenNode contentToken, GreenNode greaterThanToken)
        : this(startDocumentTypeToken, nameToken, contentToken, greaterThanToken, diagnostics: null, annotations: null)
    {
    }

    private XmlDocumentTypeSyntax(GreenNode startDocumentTypeToken, GreenNode nameToken, GreenNode contentToken, GreenNode greaterThanToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.XmlDocumentType, diagnostics, annotations)
    {
        SlotCount = 4;
        AdjustFlagsAndWidth(startDocumentTypeToken);
        _startDocumentTypeToken = startDocumentTypeToken;
        AdjustFlagsAndWidth(nameToken);
        _nameToken = nameToken;
        AdjustFlagsAndWidth(contentToken);
        _contentToken = contentToken;
        AdjustFlagsAndWidth(greaterThanToken);
        _greaterThanToken = greaterThanToken;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _startDocumentTypeToken,
        1 => _nameToken,
        2 => _contentToken,
        3 => _greaterThanToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new XmlDocumentTypeSyntax(RequiredSlot(slots[0]), RequiredSlot(slots[1]), RequiredSlot(slots[2]), RequiredSlot(slots[3]), GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new XmlDocumentTypeSyntax(_startDocumentTypeToken, _nameToken, _contentToken, _greaterThanToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new XmlDocumentTypeSyntax(_startDocumentTypeToken, _nameToken, _contentToken, _greaterThanToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Xml.XmlDocumentTypeSyntax(this, parent, position);
}
