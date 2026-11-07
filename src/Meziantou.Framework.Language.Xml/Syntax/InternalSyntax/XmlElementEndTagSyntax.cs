using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

/// <summary>The closing tag of an element.</summary>
internal sealed class XmlElementEndTagSyntax : XmlSyntaxNode
{
    private readonly GreenNode _lessThanSlashToken;
    private readonly GreenNode _nameToken;
    private readonly GreenNode? _skippedTokens;
    private readonly GreenNode _greaterThanToken;

    public XmlElementEndTagSyntax(GreenNode lessThanSlashToken, GreenNode nameToken, GreenNode? skippedTokens, GreenNode greaterThanToken)
        : this(lessThanSlashToken, nameToken, skippedTokens, greaterThanToken, diagnostics: null, annotations: null)
    {
    }

    private XmlElementEndTagSyntax(GreenNode lessThanSlashToken, GreenNode nameToken, GreenNode? skippedTokens, GreenNode greaterThanToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.XmlElementEndTag, diagnostics, annotations)
    {
        SlotCount = 4;
        AdjustFlagsAndWidth(lessThanSlashToken);
        _lessThanSlashToken = lessThanSlashToken;
        AdjustFlagsAndWidth(nameToken);
        _nameToken = nameToken;
        AdjustFlagsAndWidth(skippedTokens);
        _skippedTokens = skippedTokens;
        AdjustFlagsAndWidth(greaterThanToken);
        _greaterThanToken = greaterThanToken;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _lessThanSlashToken,
        1 => _nameToken,
        2 => _skippedTokens,
        3 => _greaterThanToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new XmlElementEndTagSyntax(RequiredSlot(slots[0]), RequiredSlot(slots[1]), slots[2], RequiredSlot(slots[3]), GetDiagnostics(), GetAnnotations());

    internal override bool IsListSlot(int index) => index is 2;
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new XmlElementEndTagSyntax(_lessThanSlashToken, _nameToken, _skippedTokens, _greaterThanToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new XmlElementEndTagSyntax(_lessThanSlashToken, _nameToken, _skippedTokens, _greaterThanToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Xml.XmlElementEndTagSyntax(this, parent, position);
}
