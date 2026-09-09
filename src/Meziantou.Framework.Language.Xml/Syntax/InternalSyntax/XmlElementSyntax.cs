using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

/// <summary>An element written with a start tag and an end tag, and whatever stands between them.</summary>
internal sealed class XmlElementSyntax : XmlNodeSyntax
{
    private readonly GreenNode _startTag;
    private readonly GreenNode? _content;
    private readonly GreenNode? _endTag;

    public XmlElementSyntax(GreenNode startTag, GreenNode? content, GreenNode? endTag)
        : this(startTag, content, endTag, diagnostics: null, annotations: null)
    {
    }

    private XmlElementSyntax(GreenNode startTag, GreenNode? content, GreenNode? endTag, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.XmlElement, diagnostics, annotations)
    {
        SlotCount = 3;
        AdjustFlagsAndWidth(startTag);
        _startTag = startTag;
        AdjustFlagsAndWidth(content);
        _content = content;
        AdjustFlagsAndWidth(endTag);
        _endTag = endTag;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _startTag,
        1 => _content,
        2 => _endTag,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new XmlElementSyntax(slots[0]!, slots[1], slots[2], GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new XmlElementSyntax(_startTag, _content, _endTag, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new XmlElementSyntax(_startTag, _content, _endTag, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Xml.XmlElementSyntax(this, parent, position);
}
