using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

/// <summary>A run of character data, which in XML includes the whitespace between tags.</summary>
internal sealed class XmlTextSyntax : XmlNodeSyntax
{
    private readonly GreenNode _textToken;

    public XmlTextSyntax(GreenNode textToken)
        : this(textToken, diagnostics: null, annotations: null)
    {
    }

    private XmlTextSyntax(GreenNode textToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.XmlText, diagnostics, annotations)
    {
        SlotCount = 1;
        AdjustFlagsAndWidth(textToken);
        _textToken = textToken;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _textToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new XmlTextSyntax(RequiredSlot(slots[0]), GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new XmlTextSyntax(_textToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new XmlTextSyntax(_textToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Xml.XmlTextSyntax(this, parent, position);
}
