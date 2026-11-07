using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Syntax;

/// <summary>The node behind a list of children.</summary>
/// <remarks>
/// A list is transparent: its elements report the list's own parent, so a consumer walking a tree never sees a list
/// node, only the children it holds.
/// </remarks>
internal sealed class SyntaxListNode : SyntaxNode
{
    private readonly SyntaxNode?[] _children;

    internal SyntaxListNode(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
        => _children = new SyntaxNode?[green.SlotCount];

    internal override SyntaxNode? GetNodeSlot(int index)
    {
        // A separated list holds its separators in the same slots as its elements; those are presented as tokens.
        if (Green.GetSlot(index) is not { IsToken: false })
            return null;

        return GetRedElement(ref _children[index], index);
    }

    internal override SyntaxNode? GetCachedSlot(int index) => _children[index];
}
