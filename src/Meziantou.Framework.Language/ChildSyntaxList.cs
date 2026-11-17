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
/// Reading a child by index walks the slots to find it, so it costs more than an array lookup. The walk is short -- it
/// is over the slots of the node, not over the children of a list -- and the position of a child already created is
/// read straight off it. Enumerating does not walk: the enumerator carries the slot and the position of the current
/// child to the next one.
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

        // The slot holds a list, so the answer is one of its children. A token needs no red node, nor the red list.
        if (greenChild.GetRequiredSlot(remaining) is { IsToken: true } greenToken)
        {
            return new SyntaxNodeOrToken(node, greenToken, position + greenChild.GetSlotOffset(remaining), index);
        }

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
    /// <remarks>
    /// The slots of the node are walked, but a slot holding a list is searched rather than walked, so a list of many
    /// thousands of elements costs a binary search instead of a scan. That matters because finding a token descends
    /// one level per step and pays this at every one of them.
    /// </remarks>
    internal static SyntaxNodeOrToken ChildThatContainsPosition(SyntaxNode node, int position, out int index)
    {
        var green = node.Green;
        var offset = position - node.Position;
        var flattened = 0;
        var running = 0;
        for (var slot = 0; offset >= 0 && slot < green.SlotCount; slot++)
        {
            if (green.GetSlot(slot) is not { } child)
                continue;

            // An empty slot ends where it starts, so this passes over it, which is what a span that contains nothing
            // means.
            if (offset < running + child.FullWidth)
            {
                index = flattened + (child is InternalSyntax.SyntaxList list ? list.FindSlotIndexContainingOffset(offset - running) : 0);

                return ItemInternal(node, index);
            }

            running += child.FullWidth;
            flattened += child.IsList ? child.SlotCount : 1;
        }

        index = -1;

        return default;
    }

    /// <summary>Walks a <see cref="ChildSyntaxList"/> without allocating.</summary>
    /// <remarks>
    /// The enumerator keeps the slot of the current child, its index in the list of the slot, and its position, so that each
    /// step moves to the next child instead of walking the slots again from the first one.
    /// </remarks>
    public struct Enumerator : IEnumerator<SyntaxNodeOrToken>
    {
        private readonly SyntaxNode? _node;
        private readonly int _count;
        private int _index;

        // The slot of the current child, the slot itself when it is not a list, its index in the list, and its position
        private int _slot;
        private GreenNode? _slotGreen;
        private int _listIndex;
        private int _position;

        internal Enumerator(SyntaxNode? node, int count)
        {
            _node = node;
            _count = count;
            _index = -1;
            _slot = -1;
        }

        public readonly SyntaxNodeOrToken Current
        {
            get
            {
                var node = _node!;
                var slotGreen = _slotGreen!;
                if (!slotGreen.IsList)
                {
                    if (node.GetNodeSlot(_slot) is { } red)
                        return red;

                    return new SyntaxNodeOrToken(node, slotGreen, _position, _index);
                }

                // A token needs no red node, nor the red list
                var element = slotGreen.GetRequiredSlot(_listIndex);
                if (!element.IsToken && node.GetNodeSlot(_slot) is { } redList && redList.GetNodeSlot(_listIndex) is { } redElement)
                    return redElement;

                return new SyntaxNodeOrToken(node, element, _position, _index);
            }
        }

        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (_index + 1 >= _count)
            {
                _index = _count;
                return false;
            }

            if (_index >= 0)
            {
                // The next child is the next one of the list, or the first child of the next slot
                if (_slotGreen!.IsList)
                {
                    _position += _slotGreen.GetRequiredSlot(_listIndex).FullWidth;
                    if (++_listIndex < _slotGreen.SlotCount)
                    {
                        _index++;
                        return true;
                    }
                }
                else
                {
                    _position += _slotGreen.FullWidth;
                }
            }
            else
            {
                _position = _node!.Position;
            }

            // An empty list has no children: its width is 0, so the position does not change
            var green = _node!.Green;
            do
            {
                _slot++;
                _slotGreen = green.GetSlot(_slot);
            }
            while (_slotGreen is null || (_slotGreen.IsList && _slotGreen.SlotCount == 0));

            _listIndex = 0;
            _index++;
            return true;
        }

        public void Reset()
        {
            _index = -1;
            _slot = -1;
            _slotGreen = null;
            _listIndex = 0;
            _position = 0;
        }

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
