namespace Meziantou.Framework.Language.InternalSyntax;

/// <summary>A sequence of children held in a single slot of its parent.</summary>
/// <remarks>
/// A list of one is represented by the element itself rather than by a list node, and an empty list by nothing at
/// all, so the common shapes cost no allocation. The red layer flattens lists when it presents children, which is
/// why a list is never visible as a node of its own.
/// </remarks>
internal sealed class SyntaxList : GreenNode
{
    /// <summary>
    /// Past this many children, the offset of each child is precomputed. Without it, walking a list would ask for
    /// offsets one at a time and re-sum the widths before each, which is quadratic in the length of the list.
    /// </summary>
    private const int PrecomputeOffsetsThreshold = 9;

    private readonly GreenNode[] _children;
    private readonly int[]? _childOffsets;

    private SyntaxList(GreenNode[] children)
        : this(children, diagnostics: null, annotations: null)
    {
    }

    private SyntaxList(GreenNode[] children, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(ListKind, diagnostics, annotations)
    {
        _children = children;
        SlotCount = children.Length;

        if (children.Length >= PrecomputeOffsetsThreshold)
        {
            _childOffsets = new int[children.Length];
        }

        for (var i = 0; i < children.Length; i++)
        {
            if (_childOffsets is not null)
            {
                _childOffsets[i] = FullWidth;
            }

            AdjustFlagsAndWidth(children[i]);
        }
    }

    public override string KindText => "List";

    /// <summary>Builds the node that holds <paramref name="children"/>, collapsing the empty and single-child cases.</summary>
    /// <returns><see langword="null"/> for no children, the child itself for one, otherwise a list.</returns>
    internal static GreenNode? List(ReadOnlySpan<GreenNode?> children)
    {
        var count = 0;
        foreach (var child in children)
        {
            if (child is not null)
            {
                count++;
            }
        }

        if (count == 0)
            return null;

        if (count == children.Length)
        {
            if (count == 1)
                return children[0];

            var copy = new GreenNode[count];
            for (var i = 0; i < count; i++)
            {
                copy[i] = children[i]!;
            }

            return new SyntaxList(copy);
        }

        var compacted = new GreenNode[count];
        var index = 0;
        foreach (var child in children)
        {
            if (child is not null)
            {
                compacted[index++] = child;
            }
        }

        return count == 1 ? compacted[0] : new SyntaxList(compacted);
    }

    /// <summary>Builds a list node even for a single child, for callers that must project the result into a red node.</summary>
    /// <remarks>
    /// <see cref="List"/> collapses a single child to the child itself, which is right for trivia and tokens because
    /// those are read straight off the green node. A sequence that has to become a red list cannot collapse to a bare
    /// token, because a token has no red node of its own.
    /// </remarks>
    internal static GreenNode? ListNode(ReadOnlySpan<GreenNode?> children)
    {
        if (List(children) is not { } collapsed)
            return null;

        if (collapsed.IsList || !collapsed.IsToken)
            return collapsed;

        return new SyntaxList([collapsed]);
    }

    /// <summary>Appends one sequence of children to another, treating a non-list as a sequence of one.</summary>
    internal static GreenNode? Concat(GreenNode? left, GreenNode? right)
    {
        if (left is null)
            return right;

        if (right is null)
            return left;

        var leftCount = left.IsList ? left.SlotCount : 1;
        var rightCount = right.IsList ? right.SlotCount : 1;
        var children = new GreenNode?[leftCount + rightCount];

        CopyTo(left, children, 0);
        CopyTo(right, children, leftCount);

        return List(children);

        static void CopyTo(GreenNode node, GreenNode?[] destination, int offset)
        {
            if (!node.IsList)
            {
                destination[offset] = node;
                return;
            }

            for (var i = 0; i < node.SlotCount; i++)
            {
                destination[offset + i] = node.GetSlot(i);
            }
        }
    }

    internal override GreenNode? GetSlot(int index) => _children[index];

    internal override int GetSlotOffset(int index)
    {
        if (_childOffsets is not null)
            return _childOffsets[index];

        return base.GetSlotOffset(index);
    }

    /// <summary>Returns the index of the child that covers <paramref name="offset"/>, which is relative to this list.</summary>
    internal int FindSlotIndexContainingOffset(int offset)
    {
        if (_childOffsets is null)
        {
            var running = 0;
            for (var i = 0; i < _children.Length; i++)
            {
                running += _children[i].FullWidth;
                if (offset < running)
                    return i;
            }

            return _children.Length - 1;
        }

        var low = 0;
        var high = _childOffsets.Length - 1;
        while (low < high)
        {
            var middle = low + ((high - low + 1) / 2);
            if (_childOffsets[middle] <= offset)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    internal override GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots) => List(slots);

    internal override GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics) => new SyntaxList(_children, diagnostics, GetAnnotations());
    internal override GreenNode SetAnnotations(SyntaxAnnotation[]? annotations) => new SyntaxList(_children, GetDiagnostics(), annotations);

    internal override SyntaxNode CreateRed(SyntaxNode? parent, int position) => new Syntax.SyntaxListNode(this, parent, position);
}
