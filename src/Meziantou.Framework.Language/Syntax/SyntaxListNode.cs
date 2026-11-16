using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Syntax;

/// <summary>The node behind a list of children.</summary>
/// <remarks>
/// A list is transparent: its elements report the list's own parent, so a consumer walking a tree never sees a list
/// node, only the children it holds.
/// </remarks>
internal sealed class SyntaxListNode : SyntaxNode
{
    // The red nodes of the elements, created when the first one is: a list of tokens has none
    private SyntaxNode?[]? _children;

    internal SyntaxListNode(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    internal override SyntaxNode? GetNodeSlot(int index)
    {
        // A separated list holds its separators in the same slots as its elements; those are presented as tokens.
        if (Green.GetSlot(index) is not { IsToken: false })
            return null;

        var children = Volatile.Read(ref _children);
        if (children is null)
        {
            // The array that won the race, not the one this thread created, so every caller caches in the same one
            children = Interlocked.CompareExchange(ref _children, new SyntaxNode?[Green.SlotCount], comparand: null) ?? _children;
        }

        return GetRedElement(ref children[index], index);
    }

    internal override SyntaxNode? GetCachedSlot(int index) => Volatile.Read(ref _children)?[index];

    // The green list knows where its children are: walking back to the previous red sibling is linear for the tokens of a list,
    // which have none, and makes enumerating a long list of tokens quadratic
    internal override int GetChildPosition(int index) => Position + Green.GetSlotOffset(index);
}
