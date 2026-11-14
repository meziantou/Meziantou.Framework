using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Markdown;

/// <summary>A node of a Markdown syntax tree: a block or an inline, whose <see cref="Kind"/> says which.</summary>
public sealed class MarkdownSyntaxNode : SyntaxNode
{
    private SyntaxNode? _children;

    internal MarkdownSyntaxNode(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the kind of the node.</summary>
    public SyntaxKind Kind() => (SyntaxKind)RawKind;

    /// <summary>Gets the parent of the node, or <see langword="null"/> for the document.</summary>
    public new MarkdownSyntaxNode? Parent => (MarkdownSyntaxNode?)base.Parent;

    // The only slot is the list of the children
    internal override SyntaxNode? GetNodeSlot(int index) => index == 0 ? GetRedAtZero(ref _children) : null;

    internal override SyntaxNode? GetCachedSlot(int index) => index == 0 ? _children : null;
}
