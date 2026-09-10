using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

/// <summary>Text the parser could make nothing of, kept so the document still reproduces its source.</summary>
internal sealed class XmlSkippedTextSyntax : XmlNodeSyntax
{
    private readonly GreenNode? _tokens;

    public XmlSkippedTextSyntax(GreenNode? tokens)
        : this(tokens, diagnostics: null, annotations: null)
    {
    }

    private XmlSkippedTextSyntax(GreenNode? tokens, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(SyntaxKind.XmlSkippedText, diagnostics, annotations)
    {
        SlotCount = 1;
        AdjustFlagsAndWidth(tokens);
        _tokens = tokens;
    }

    internal override GreenNode? GetSlot(int index) => index switch
    {
        0 => _tokens,
        _ => null,
    };

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => new XmlSkippedTextSyntax(slots[0], GetDiagnostics(), GetAnnotations());

    internal override bool IsListSlot(int index) => index is 0;
    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new XmlSkippedTextSyntax(_tokens, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new XmlSkippedTextSyntax(_tokens, GetDiagnostics(), annotations);
    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Xml.XmlSkippedTextSyntax(this, parent, position);
}
