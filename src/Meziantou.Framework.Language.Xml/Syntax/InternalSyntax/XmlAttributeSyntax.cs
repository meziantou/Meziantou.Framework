using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

/// <summary>An attribute of a tag: a name, an equals sign, and a quoted value.</summary>
internal sealed class XmlAttributeSyntax : XmlSyntaxNode
{
    private readonly GreenNode _nameToken;
    private readonly GreenNode _equalsToken;
    private readonly GreenNode _startQuoteToken;
    private readonly GreenNode _valueToken;
    private readonly GreenNode _endQuoteToken;

    public XmlAttributeSyntax(GreenNode nameToken, GreenNode equalsToken, GreenNode startQuoteToken, GreenNode valueToken, GreenNode endQuoteToken)
        : this(nameToken, equalsToken, startQuoteToken, valueToken, endQuoteToken, diagnostics: null, annotations: null)
    {
    }

    private XmlAttributeSyntax(GreenNode nameToken, GreenNode equalsToken, GreenNode startQuoteToken, GreenNode valueToken, GreenNode endQuoteToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.XmlAttribute, diagnostics, annotations)
    {
        SlotCount = 5;
        AdjustFlagsAndWidth(nameToken);
        _nameToken = nameToken;
        AdjustFlagsAndWidth(equalsToken);
        _equalsToken = equalsToken;
        AdjustFlagsAndWidth(startQuoteToken);
        _startQuoteToken = startQuoteToken;
        AdjustFlagsAndWidth(valueToken);
        _valueToken = valueToken;
        AdjustFlagsAndWidth(endQuoteToken);
        _endQuoteToken = endQuoteToken;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _nameToken,
        1 => _equalsToken,
        2 => _startQuoteToken,
        3 => _valueToken,
        4 => _endQuoteToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new XmlAttributeSyntax(slots[0]!, slots[1]!, slots[2]!, slots[3]!, slots[4]!, GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new XmlAttributeSyntax(_nameToken, _equalsToken, _startQuoteToken, _valueToken, _endQuoteToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new XmlAttributeSyntax(_nameToken, _equalsToken, _startQuoteToken, _valueToken, _endQuoteToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Xml.XmlAttributeSyntax(this, parent, position);
}
