using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

/// <summary>A comment.</summary>
internal sealed class XmlCommentSyntax : XmlNodeSyntax
{
    private readonly GreenNode _startCommentToken;
    private readonly GreenNode _textToken;
    private readonly GreenNode _endCommentToken;

    public XmlCommentSyntax(GreenNode startCommentToken, GreenNode textToken, GreenNode endCommentToken)
        : this(startCommentToken, textToken, endCommentToken, diagnostics: null, annotations: null)
    {
    }

    private XmlCommentSyntax(GreenNode startCommentToken, GreenNode textToken, GreenNode endCommentToken, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.XmlComment, diagnostics, annotations)
    {
        SlotCount = 3;
        AdjustFlagsAndWidth(startCommentToken);
        _startCommentToken = startCommentToken;
        AdjustFlagsAndWidth(textToken);
        _textToken = textToken;
        AdjustFlagsAndWidth(endCommentToken);
        _endCommentToken = endCommentToken;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _startCommentToken,
        1 => _textToken,
        2 => _endCommentToken,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new XmlCommentSyntax(RequiredSlot(slots[0]), RequiredSlot(slots[1]), RequiredSlot(slots[2]), GetDiagnostics(), GetAnnotations());
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new XmlCommentSyntax(_startCommentToken, _textToken, _endCommentToken, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new XmlCommentSyntax(_startCommentToken, _textToken, _endCommentToken, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Xml.XmlCommentSyntax(this, parent, position);
}
