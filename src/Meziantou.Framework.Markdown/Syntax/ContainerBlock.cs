// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Syntax;

/// <summary>
/// A base class for container blocks.
/// </summary>
/// <seealso cref="Block" />
[DebuggerDisplay("{GetType().Name} Count = {Count}")]
public abstract class ContainerBlock : Block, IList<Block>, IReadOnlyList<Block>
{
    private BlockWrapper[] _children;

    // The children are _children[0.._gapStart) followed by _children[_gapStart + _gapLength..Count + _gapLength). The gap is
    // left where the last child was inserted, so inserting children one after the other in the middle of a container is
    // linear, like the markdown parser does when a block adds blocks after itself. When there is a gap, it holds all the free
    // slots of the array.
    private int _gapStart;
    private int _gapLength;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContainerBlock"/> class.
    /// </summary>
    /// <param name="parser">The parser used to create this block.</param>
    protected ContainerBlock(BlockParser? parser) : base(parser)
    {
        _children = [];
        SetTypeKind(isInline: false, isContainer: true);
    }

    /// <summary>
    /// Gets the last child.
    /// </summary>
    public Block? LastChild
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            int index = Count - 1;
            if (index < 0)
            {
                return null;
            }

            return _children[PhysicalIndex(index)].Block;
        }
    }

    /// <summary>
    /// Specialize enumerator.
    /// </summary>
    /// <returns></returns>
    public Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    IEnumerator<Block> IEnumerable<Block>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>
    /// Performs the add operation.
    /// </summary>
    public void Add(Block item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Parent != null)
        {
            ThrowHelper.ArgumentException("Cannot add this block as it as already attached to another container (block.Parent != null)");
        }

        AddLast(item);
        item.Parent = this;

        UpdateSpanEnd(item.Span.End);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int PhysicalIndex(int index) => index < _gapStart ? index : index + _gapLength;

    private void Grow()
    {
        if (_children.Length == 0)
        {
            _children = new BlockWrapper[4];
        }
        else
        {
            Debug.Assert(_gapLength == 0);

            var newArray = new BlockWrapper[_children.Length * 2];
            Array.Copy(_children, 0, newArray, 0, Count);
            _children = newArray;
        }
    }

    // Stores the block after the last child. The free slots are in the gap when there is one, so the gap is moved to the end
    // and the block takes its first slot.
    private void AddLast(Block item)
    {
        if (_gapLength > 0)
        {
            MoveGap(Count);
            _children[_gapStart] = new BlockWrapper(item);
            _gapStart++;
            _gapLength--;
        }
        else
        {
            if (Count == _children.Length)
            {
                Grow();
            }

            _children[Count] = new BlockWrapper(item);
        }

        Count++;
    }

    // Moves the gap to the given index, opening a gap there when there is none
    private void MoveGap(int index)
    {
        if (_gapLength == 0)
        {
            // Use at least as many free slots as there are children, so that the gap lasts for the next insertions
            if (_children.Length - Count < Math.Max(4, Count))
            {
                var newArray = new BlockWrapper[Math.Max(8, Count * 2)];
                var gapLength = newArray.Length - Count;
                Array.Copy(_children, 0, newArray, 0, index);
                Array.Copy(_children, index, newArray, index + gapLength, Count - index);
                _children = newArray;
                _gapStart = index;
                _gapLength = gapLength;
                return;
            }

            var free = _children.Length - Count;
            Array.Copy(_children, index, _children, index + free, Count - index);
            Array.Clear(_children, index, Math.Min(free, Count - index));
            _gapStart = index;
            _gapLength = free;
            return;
        }

        // Only the slots of the moved children that become part of the gap are cleared: the other ones already are, and
        // clearing the whole gap would make each move proportional to the number of free slots
        if (index < _gapStart)
        {
            var count = _gapStart - index;
            Array.Copy(_children, index, _children, index + _gapLength, count);
            Array.Clear(_children, index, Math.Min(count, _gapLength));
        }
        else if (index > _gapStart)
        {
            var count = index - _gapStart;
            var cleared = Math.Min(count, _gapLength);
            Array.Copy(_children, _gapStart + _gapLength, _children, _gapStart, count);
            Array.Clear(_children, index + _gapLength - cleared, cleared);
        }

        _gapStart = index;
    }

    private void CloseGap()
    {
        if (_gapLength > 0)
        {
            Array.Copy(_children, _gapStart + _gapLength, _children, _gapStart, Count - _gapStart);
            Array.Clear(_children, Count, _gapLength);
            _gapStart = 0;
            _gapLength = 0;
        }
    }

    /// <summary>
    /// Performs the clear operation.
    /// </summary>
    public void Clear()
    {
        for (int i = 0; i < Count; i++)
        {
            _children[PhysicalIndex(i)].Block.Parent = null;
        }

        Array.Clear(_children);
        Count = 0;
        _gapStart = 0;
        _gapLength = 0;
    }

    /// <summary>
    /// Transfers all children from this container to <paramref name="destination"/>.
    /// </summary>
    /// <param name="destination">The destination container.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="destination"/> is null.</exception>
    /// <remarks>
    /// Child order is preserved. This method does not recompute source or destination spans/trivia.
    /// </remarks>
    public void TransferChildrenTo(ContainerBlock destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (ReferenceEquals(this, destination))
        {
            return;
        }

        CloseGap();
        var children = _children;
        int count = Count;
        for (int i = 0; i < count && i < children.Length; i++)
        {
            var child = children[i].Block;
            child.Parent = null;
            children[i] = default;
            destination.Add(child);
        }

        Count = 0;
    }

    /// <summary>
    /// Performs the contains operation.
    /// </summary>
    public bool Contains(Block item)
    {
        return IndexOf(item) >= 0;
    }

    /// <summary>
    /// Performs the copy to operation.
    /// </summary>
    public void CopyTo(Block[] array, int arrayIndex)
    {
        for (int i = 0; i < Count; i++)
        {
            array[arrayIndex + i] = _children[PhysicalIndex(i)].Block;
        }
    }

    /// <summary>
    /// Performs the remove operation.
    /// </summary>
    public bool Remove(Block item)
    {
        // A block is usually removed just after it was added (for example, a paragraph replaced by a setext heading), so
        // search from the end: searching from the start makes a document of many such blocks quadratic
        int index = LastIndexOf(item);
        if (index >= 0)
        {
            RemoveAt(index);
            return true;
        }
        return false;
    }

    internal int LastIndexOf(Block item)
    {
        ArgumentNullException.ThrowIfNull(item);

        for (int i = Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_children[PhysicalIndex(i)].Block, item))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Gets or sets the count.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Gets or sets the is read only.
    /// </summary>
    public bool IsReadOnly => false;

    /// <summary>
    /// Performs the index of operation.
    /// </summary>
    public int IndexOf(Block item)
    {
        ArgumentNullException.ThrowIfNull(item);

        for (int i = 0; i < Count; i++)
        {
            if (ReferenceEquals(_children[PhysicalIndex(i)].Block, item))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Performs the insert operation.
    /// </summary>
    public void Insert(int index, Block item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Parent != null)
        {
            ThrowHelper.ArgumentException("Cannot add this block as it as already attached to another container (block.Parent != null)");
        }
        if ((uint)index > (uint)Count)
        {
            ThrowHelper.ArgumentOutOfRangeException_index();
        }

        if (index == Count)
        {
            AddLast(item);
        }
        else
        {
            MoveGap(index);
            _children[_gapStart] = new BlockWrapper(item);
            _gapStart++;
            _gapLength--;
            Count++;
        }

        item.Parent = this;
    }

    /// <summary>
    /// Removes at.
    /// </summary>
    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)Count)
            ThrowHelper.ArgumentOutOfRangeException_index();

        _children[PhysicalIndex(index)].Block.Parent = null;
        if (_gapLength == 0)
        {
            Count--;
            if (index < Count)
            {
                Array.Copy(_children, index + 1, _children, index, Count - index);
            }
            _children[Count] = default;
        }
        else
        {
            // The removed child is added to the gap
            MoveGap(index);
            _children[_gapStart + _gapLength] = default;
            _gapLength++;
            Count--;
        }
    }

    /// <summary>
    /// Gets the value at the specified index.
    /// </summary>
    public Block this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)Count)
            {
                ThrowHelper.ThrowIndexOutOfRangeException();
                return null;
            }
            return _children[PhysicalIndex(index)].Block;
        }
        set
        {
            if ((uint)index >= (uint)Count) ThrowHelper.ThrowIndexOutOfRangeException();

            ArgumentNullException.ThrowIfNull(value);

            if (value.Parent != null)
                ThrowHelper.ArgumentException("Cannot add this block as it as already attached to another container (block.Parent != null)");

            var existingChild = _children[PhysicalIndex(index)].Block;
            if (existingChild != null)
                existingChild.Parent = null;

            value.Parent = this;
            _children[PhysicalIndex(index)] = new BlockWrapper(value);
        }
    }

    /// <summary>
    /// Checks whether this container span is valid with respect to child block spans.
    /// </summary>
    /// <param name="recursive">
    /// When <c>true</c>, validates descendant container blocks and inline containers recursively.
    /// </param>
    /// <returns>
    /// <c>true</c> when this container span contains all direct child spans and recursive checks (if enabled) succeed;
    /// otherwise, <c>false</c>.
    /// </returns>
    public bool HasValidSpan(bool recursive = false)
    {
        for (int i = 0; i < Count; i++)
        {
            var child = _children[PhysicalIndex(i)].Block;
            if (!ContainsSpan(Span, child.Span))
            {
                return false;
            }

            if (!recursive)
            {
                continue;
            }

            if (child is ContainerBlock containerBlock)
            {
                if (!containerBlock.HasValidSpan(recursive: true))
                {
                    return false;
                }
            }
            else if (child is LeafBlock leafBlock && leafBlock.Inline is ContainerInline inline)
            {
                if (!ContainsSpan(leafBlock.Span, inline.Span))
                {
                    return false;
                }

                if (!inline.HasValidSpan(recursive: true))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Updates this container span from its child block spans.
    /// </summary>
    /// <param name="recursive">
    /// When <c>true</c>, updates descendant container blocks and inline containers recursively before updating this container.
    /// </param>
    /// <param name="preserveSelfSpan">
    /// When <c>true</c>, preserves this container current span and only expands it to include children.
    /// When <c>false</c>, recomputes from children only.
    /// </param>
    /// <returns><c>true</c> when this container span changed; otherwise, <c>false</c>.</returns>
    public bool UpdateSpanFromChildren(bool recursive = false, bool preserveSelfSpan = true)
    {
        var updatedSpan = SourceSpan.Empty;
        bool hasUpdatedSpan = false;

        if (preserveSelfSpan && !Span.IsEmpty)
        {
            updatedSpan = Span;
            hasUpdatedSpan = true;
        }

        for (int i = 0; i < Count; i++)
        {
            var child = _children[PhysicalIndex(i)].Block;

            if (recursive)
            {
                if (child is ContainerBlock containerBlock)
                {
                    containerBlock.UpdateSpanFromChildren(recursive: true, preserveSelfSpan: preserveSelfSpan);
                }
                else if (child is LeafBlock leafBlock && leafBlock.Inline is ContainerInline inline)
                {
                    inline.UpdateSpanFromChildren(recursive: true, preserveSelfSpan: preserveSelfSpan);

                    if (!ContainsSpan(leafBlock.Span, inline.Span))
                    {
                        if (preserveSelfSpan && !leafBlock.Span.IsEmpty)
                        {
                            leafBlock.UpdateSpanToInclude(inline.Span);
                        }
                        else
                        {
                            leafBlock.Span = inline.Span;
                        }
                    }
                }
            }

            AppendSpan(ref updatedSpan, ref hasUpdatedSpan, child.Span);
        }

        if (!hasUpdatedSpan)
        {
            updatedSpan = SourceSpan.Empty;
        }

        if (updatedSpan == Span)
        {
            return false;
        }

        Span = updatedSpan;
        return true;
    }

    /// <summary>
    /// Performs the sort operation.
    /// </summary>
    public void Sort(IComparer<Block> comparer)
    {
        ArgumentNullException.ThrowIfNull(comparer);
        CloseGap();
        Array.Sort(_children, 0, Count, new BlockComparerWrapper(comparer));
    }

    /// <summary>
    /// Performs the sort operation.
    /// </summary>
    public void Sort(Comparison<Block> comparison)
    {
        ArgumentNullException.ThrowIfNull(comparison);
        CloseGap();
        Array.Sort(_children, 0, Count, new BlockComparisonWrapper(comparison));
    }

    #region Nested type: Enumerator

    /// <summary>
    /// Represents the Enumerator type.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Enumerator : IEnumerator<Block>
    {
        private readonly ContainerBlock _block;
        private int _index;
        private Block? _current;

        internal Enumerator(ContainerBlock block)
        {
            this._block = block;
            _index = 0;
            _current = null;
        }

        /// <summary>
        /// Gets or sets the current.
        /// </summary>
        public Block Current => _current!;

        object IEnumerator.Current => Current;

        /// <summary>
        /// Performs the dispose operation.
        /// </summary>
        public void Dispose()
        {
        }

        /// <summary>
        /// Performs the move next operation.
        /// </summary>
        public bool MoveNext()
        {
            if (_index < _block.Count)
            {
                _current = _block[_index];
                _index++;
                return true;
            }
            return MoveNextRare();
        }

        private bool MoveNextRare()
        {
            _index = _block.Count + 1;
            _current = null;
            return false;
        }

        void IEnumerator.Reset()
        {
            _index = 0;
            _current = null;
        }
    }

    #endregion

    private sealed class BlockComparisonWrapper(Comparison<Block> comparison) : IComparer<BlockWrapper>
    {
        private readonly Comparison<Block> _comparison = comparison;

        public int Compare(BlockWrapper x, BlockWrapper y)
        {
            return _comparison(x.Block, y.Block);
        }
    }

    private sealed class BlockComparerWrapper(IComparer<Block> comparer) : IComparer<BlockWrapper>
    {
        private readonly IComparer<Block> _comparer = comparer;

        public int Compare(BlockWrapper x, BlockWrapper y)
        {
            return _comparer.Compare(x.Block, y.Block);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ContainsSpan(in SourceSpan containerSpan, in SourceSpan childSpan)
    {
        return childSpan.IsEmpty || (!containerSpan.IsEmpty && childSpan.Start >= containerSpan.Start && childSpan.End <= containerSpan.End);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AppendSpan(ref SourceSpan destinationSpan, ref bool hasDestinationSpan, in SourceSpan spanToAppend)
    {
        if (spanToAppend.IsEmpty)
        {
            return;
        }

        if (!hasDestinationSpan)
        {
            destinationSpan = spanToAppend;
            hasDestinationSpan = true;
            return;
        }

        if (spanToAppend.Start < destinationSpan.Start)
        {
            destinationSpan.Start = spanToAppend.Start;
        }

        if (spanToAppend.End > destinationSpan.End)
        {
            destinationSpan.End = spanToAppend.End;
        }
    }
}
