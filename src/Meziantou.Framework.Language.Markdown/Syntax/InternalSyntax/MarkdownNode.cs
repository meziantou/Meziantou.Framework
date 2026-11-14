using Meziantou.Framework.Language.InternalSyntax;
using Green = Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Markdown.Syntax.InternalSyntax;

/// <summary>
/// The green node of every Markdown construct: a kind and its children, tokens or nodes, in the order of the text. A typed
/// API reads the children by kind, so a new construct only needs a new kind.
/// </summary>
/// <remarks>
/// The children are in one list slot: the red tree finds a child of a list in constant time, so walking the thousands of
/// blocks of a document is linear.
/// </remarks>
internal sealed class MarkdownNode : GreenNode
{
    private readonly GreenNode? _children;

    public MarkdownNode(SyntaxKind kind, ReadOnlySpan<GreenNode?> children)
        : this(kind, Green.SyntaxList.ListNode(children), diagnostics: null, annotations: null)
    {
    }

    private MarkdownNode(SyntaxKind kind, GreenNode? children, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base((int)kind, diagnostics, annotations)
    {
        _children = children;
        SlotCount = 1;
        AdjustFlagsAndWidth(children);
    }

    public override string KindText => ((SyntaxKind)RawKind).ToString();

    internal override GreenNode? GetSlot(int index) => index == 0 ? _children : null;

    internal override bool IsListSlot(int index) => index == 0;

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots)
    {
        // The slot is the list of the children, or a single child that replaced it
        var children = slots[0] switch
        {
            null => null,
            { IsList: true } list => list,
            var child => Green.SyntaxList.ListNode([child]),
        };

        return new MarkdownNode((SyntaxKind)RawKind, children, GetDiagnostics(), GetAnnotations());
    }

    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new MarkdownNode((SyntaxKind)RawKind, _children, diagnostics, GetAnnotations());

    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new MarkdownNode((SyntaxKind)RawKind, _children, GetDiagnostics(), annotations);

    internal override bool IsEndOfLineTrivia(GreenNode trivia) => trivia.RawKind == (int)SyntaxKind.EndOfLineTrivia;

    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Markdown.MarkdownSyntaxNode(this, parent, position);
}
