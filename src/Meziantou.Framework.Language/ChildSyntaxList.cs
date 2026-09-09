using System.Collections;
using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language;

/// <summary>The children of a node, with the lists among them flattened away.</summary>
/// <remarks>
/// <para>
/// A node holds its children in numbered slots, and a slot may hold a whole list. This type presents both cases the
/// same way, so a caller sees a flat sequence of nodes and tokens and never a list of its own.
/// </para>
/// <para>
/// The indexer walks the slots to find its answer, so it costs more than an array lookup. Enumerating carries the
/// running position and slot from one step to the next, so prefer <c>foreach</c> over indexing in a loop.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct ChildSyntaxList : IReadOnlyList<SyntaxNodeOrToken>, IEquatable<ChildSyntaxList>
{
    private readonly SyntaxNode? _node;

    internal ChildSyntaxList(SyntaxNode node)
    {
        _node = node;
        Count = CountChildren(node.Green);
    }

    public int Count { get; }

    public SyntaxNodeOrToken this[int index]
    {
        get
        {
            if (_node is null || (uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return ItemInternal(_node, index);
        }
    }

    public bool Any() => Count > 0;
    public SyntaxNodeOrToken First() => Count > 0 ? this[0] : throw new InvalidOperationException("The node has no children.");
    public SyntaxNodeOrToken Last() => Count > 0 ? this[Count - 1] : throw new InvalidOperationException("The node has no children.");
    public SyntaxNodeOrToken FirstOrDefault() => Count > 0 ? this[0] : default;
    public SyntaxNodeOrToken LastOrDefault() => Count > 0 ? this[Count - 1] : default;

    public Enumerator GetEnumerator() => new(_node, Count);
    IEnumerator<SyntaxNodeOrToken> IEnumerable<SyntaxNodeOrToken>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Enumerates the children from last to first.</summary>
    public Reversed Reverse() => new(_node, Count);

    public bool Equals(ChildSyntaxList other) => ReferenceEquals(_node, other._node);
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ChildSyntaxList other && Equals(other);
    public override int GetHashCode() => _node?.GetHashCode() ?? 0;
    public static bool operator ==(ChildSyntaxList left, ChildSyntaxList right) => left.Equals(right);
    public static bool operator !=(ChildSyntaxList left, ChildSyntaxList right) => !left.Equals(right);

    /// <summary>Counts the children of <paramref name="green"/>, counting a list slot as its own children.</summary>
    internal static int CountChildren(GreenNode green)
    {
        var count = 0;
        for (var i = 0; i < green.SlotCount; i++)
        {
            if (green.GetSlot(i) is { } child)
            {
                count += child.IsList ? child.SlotCount : 1;
            }
        }

        return count;
    }

    /// <summary>Returns the child at <paramref name="index"/> in the flattened sequence.</summary>
    internal static SyntaxNodeOrToken ItemInternal(SyntaxNode node, int index)
    {
        var green = node.Green;
        var remaining = index;
        var slotIndex = 0;
        var position = node.Position;
        GreenNode? greenChild;

        // Walk the slots, treating a list slot as occupying as many places as it has children.
        while (true)
        {
            greenChild = green.GetSlot(slotIndex);
            if (greenChild is not null)
            {
                var occupancy = greenChild.IsList ? greenChild.SlotCount : 1;
                if (remaining < occupancy)
                    break;

                remaining -= occupancy;
                position += greenChild.FullWidth;
            }

            slotIndex++;
        }

        if (!greenChild.IsList)
        {
            if (node.GetNodeSlot(slotIndex) is { } red)
                return red;

            return new SyntaxNodeOrToken(node, greenChild, node.GetChildPosition(slotIndex), index);
        }

        // The slot holds a list, so the answer is one of its children.
        if (node.GetNodeSlot(slotIndex) is { } redList)
        {
            if (redList.GetNodeSlot(remaining) is { } redElement)
                return redElement;

            return new SyntaxNodeOrToken(node, greenChild.GetRequiredSlot(remaining), redList.GetChildPosition(remaining), index);
        }

        position += greenChild.GetSlotOffset(remaining);

        return new SyntaxNodeOrToken(node, greenChild.GetRequiredSlot(remaining), position, index);
    }

    /// <summary>Returns the child whose full span contains <paramref name="position"/>.</summary>
    internal static SyntaxNodeOrToken ChildThatContainsPosition(SyntaxNode node, int position, out int index)
    {
        var count = CountChildren(node.Green);
        for (index = 0; index < count; index++)
        {
            var child = ItemInternal(node, index);
            if (child.FullSpan.Contains(position))
                return child;
        }

        index = -1;

        return default;
    }

    /// <summary>Walks a <see cref="ChildSyntaxList"/> without allocating.</summary>
    public struct Enumerator : IEnumerator<SyntaxNodeOrToken>
    {
        private readonly SyntaxNode? _node;
        private readonly int _count;
        private int _index;

        internal Enumerator(SyntaxNode? node, int count)
        {
            _node = node;
            _count = count;
            _index = -1;
        }

        public readonly SyntaxNodeOrToken Current => ItemInternal(_node!, _index);
        readonly object IEnumerator.Current => Current;

        public bool MoveNext() => ++_index < _count;
        public void Reset() => _index = -1;
        public readonly void Dispose() { }
    }

    /// <summary>The children of a node, from last to first.</summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly struct Reversed : IEnumerable<SyntaxNodeOrToken>, IEquatable<Reversed>
    {
        private readonly SyntaxNode? _node;
        private readonly int _count;

        internal Reversed(SyntaxNode? node, int count)
        {
            _node = node;
            _count = count;
        }

        public Enumerator GetEnumerator() => new(_node, _count);
        IEnumerator<SyntaxNodeOrToken> IEnumerable<SyntaxNodeOrToken>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool Equals(Reversed other) => ReferenceEquals(_node, other._node) && _count == other._count;
        public override bool Equals([NotNullWhen(true)] object? obj) => obj is Reversed other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_node, _count);
        public static bool operator ==(Reversed left, Reversed right) => left.Equals(right);
        public static bool operator !=(Reversed left, Reversed right) => !left.Equals(right);

        /// <summary>Walks a <see cref="Reversed"/> without allocating.</summary>
        public struct Enumerator : IEnumerator<SyntaxNodeOrToken>
        {
            private readonly SyntaxNode? _node;
            private readonly int _count;
            private int _index;

            internal Enumerator(SyntaxNode? node, int count)
            {
                _node = node;
                _count = count;
                _index = count;
            }

            public readonly SyntaxNodeOrToken Current => ItemInternal(_node!, _index);
            readonly object IEnumerator.Current => Current;

            public bool MoveNext() => --_index >= 0;
            public void Reset() => _index = _count;
            public readonly void Dispose() { }
        }
    }
}
