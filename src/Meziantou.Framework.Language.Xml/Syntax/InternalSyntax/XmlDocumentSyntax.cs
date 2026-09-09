using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

/// <summary>Everything a document is made of, in the order it was written.</summary>
internal sealed class XmlDocumentSyntax : XmlSyntaxNode
{
    private readonly GreenNode? _nodes;
    private readonly GreenNode _endOfFileToken;

    public XmlDocumentSyntax(GreenNode? nodes, GreenNode endOfFileToken)
        : this(nodes, endOfFileToken, diagnostics: null, annotations: null)
    {
    }

    private XmlDocumentSyntax(GreenNode? nodes, GreenNode endOfFileToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.XmlDocument, diagnostics, annotations)
    {
        SlotCount = 2;
        AdjustFlagsAndWidth(nodes);
        _nodes = nodes;
        AdjustFlagsAndWidth(endOfFileToken);
        _endOfFileToken = endOfFileToken;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _nodes,
        1 => _endOfFileToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new XmlDocumentSyntax(slots[0], RequiredSlot(slots[1]), GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new XmlDocumentSyntax(_nodes, _endOfFileToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new XmlDocumentSyntax(_nodes, _endOfFileToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Xml.XmlDocumentSyntax(this, parent, position);
}
