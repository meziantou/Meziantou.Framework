using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

/// <summary>A CDATA section, whose text is taken literally.</summary>
internal sealed class XmlCDataSectionSyntax : XmlNodeSyntax
{
    private readonly GreenNode _startCDataToken;
    private readonly GreenNode _textToken;
    private readonly GreenNode _endCDataToken;

    public XmlCDataSectionSyntax(GreenNode startCDataToken, GreenNode textToken, GreenNode endCDataToken)
        : this(startCDataToken, textToken, endCDataToken, diagnostics: null, annotations: null)
    {
    }

    private XmlCDataSectionSyntax(GreenNode startCDataToken, GreenNode textToken, GreenNode endCDataToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.XmlCDataSection, diagnostics, annotations)
    {
        SlotCount = 3;
        AdjustFlagsAndWidth(startCDataToken);
        _startCDataToken = startCDataToken;
        AdjustFlagsAndWidth(textToken);
        _textToken = textToken;
        AdjustFlagsAndWidth(endCDataToken);
        _endCDataToken = endCDataToken;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _startCDataToken,
        1 => _textToken,
        2 => _endCDataToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new XmlCDataSectionSyntax(RequiredSlot(slots[0]), RequiredSlot(slots[1]), RequiredSlot(slots[2]), GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new XmlCDataSectionSyntax(_startCDataToken, _textToken, _endCDataToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new XmlCDataSectionSyntax(_startCDataToken, _textToken, _endCDataToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Xml.XmlCDataSectionSyntax(this, parent, position);
}
