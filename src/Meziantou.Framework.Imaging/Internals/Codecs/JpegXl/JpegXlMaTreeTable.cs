using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A meta-adaptive tree (ISO/IEC 18181-1, section H.4.2) evaluated without walking it, when its decisions test the properties
/// that the position of a sample and its neighbors give (2 to 15, but the property 8, which depends on the previous sample)
/// and it has at most 256 leaves. The leaves are numbered from left to right. A decision whose test is false for a sample
/// rules out the leaves of its left child (those of the values above the split value), and the leaf of the sample is the
/// first leaf that no false decision rules out: on the path to it, the sample goes right exactly at its false decisions, and
/// every leaf before it is in the left child of one of them.
/// </summary>
/// <remarks>
/// The decisions on a property that are false for a value are the same for every value of an interval of its split values:
/// each property has a table that gives, for each value from its smallest split value to its largest one plus one, the
/// mask of the leaves that its false decisions leave (one bit per leaf); a smaller or a larger value has the mask of the
/// first or the last entry. The leaf of a sample is the first bit set in the masks of its properties, combined: it takes no
/// branch that depends on the values, which are not predictable.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly ref struct JpegXlMaTreeTable
{
    /// <summary>The number of values of the buffer of a table.</summary>
    public const int BufferLength = (MaxPropertyTableLength * MaxWordCount) + MaxLeafCount;

    /// <summary>The number of values of the memory of the properties of a table.</summary>
    public const int SlotBufferLength = MaxPropertyCount;

    private const int MaxPropertyCount = 8;
    private const int MaxLeafCount = 256;
    private const int MaxNodeCount = (2 * MaxLeafCount) - 1;
    private const int MaxPropertyTableLength = 2048;
    private const int MaxWordCount = MaxLeafCount / 64;

    private readonly ReadOnlySpan<ulong> _buffer;
    private readonly ReadOnlySpan<Slot> _slots;
    private readonly int _leafOffset;
    private readonly bool _isWide;

    private JpegXlMaTreeTable(ReadOnlySpan<ulong> buffer, ReadOnlySpan<Slot> slots, int leafOffset, bool isWide)
    {
        _buffer = buffer;
        _slots = slots;
        _leafOffset = leafOffset;
        _isWide = isWide;
    }

    /// <summary>
    /// Builds the table of a tree (specialized for a channel, see <see cref="JpegXlModularDecoder.Specialize"/>) when its
    /// decisions allow it, with the cluster of the context of each leaf.
    /// </summary>
    /// <param name="nodes">The nodes of the tree, each child after its parent.</param>
    /// <param name="decoder">The decoder of the samples, which gives the cluster of each context.</param>
    /// <param name="buffer">The memory of the table: <see cref="BufferLength"/> values.</param>
    /// <param name="slotBuffer">The memory of the properties of the table: <see cref="SlotBufferLength"/> values.</param>
    /// <param name="table">Receives the table.</param>
    /// <returns><see langword="false"/> when the tree cannot be a table: it tests other properties, more of them, or is too large.</returns>
    public static bool TryCreate(ReadOnlySpan<JpegXlMaTreeNode> nodes, JpegXlEntropyDecoder decoder, Span<ulong> buffer, Span<Slot> slotBuffer, out JpegXlMaTreeTable table)
    {
        table = default;
        if (!IsCandidate(nodes))
            return false;

        // The properties, the slot of each one, and the range of their split values
        Span<int> slotOfProperty = stackalloc int[JpegXlMaTree.WeightedProperty + 1];
        slotOfProperty.Fill(-1);
        Span<int> properties = stackalloc int[MaxPropertyCount];
        Span<int> smallest = stackalloc int[MaxPropertyCount];
        Span<int> largest = stackalloc int[MaxPropertyCount];
        var propertyCount = 0;
        foreach (ref readonly var node in nodes)
        {
            if (node.IsLeaf)
                continue;

            var slot = slotOfProperty[node.Property];
            if (slot < 0)
            {
                slot = propertyCount++;
                slotOfProperty[node.Property] = slot;
                properties[slot] = node.Property;
                smallest[slot] = node.ValueOrOffset;
                largest[slot] = node.ValueOrOffset;
            }

            smallest[slot] = Math.Min(smallest[slot], node.ValueOrOffset);
            largest[slot] = Math.Max(largest[slot], node.ValueOrOffset);
        }

        // The leaves of each subtree, numbered from left to right: the leaves under each node are counted from the last
        // node (the children follow their parent), then each node gives its first leaf to its children
        Span<int> leafCounts = stackalloc int[nodes.Length];
        Span<int> firstLeaves = stackalloc int[nodes.Length];
        for (var i = nodes.Length - 1; i >= 0; i--)
        {
            ref readonly var node = ref nodes[i];
            leafCounts[i] = node.IsLeaf ? 1 : leafCounts[node.LeftOrContext] + leafCounts[node.RightOrPredictor];
        }

        var leafCount = leafCounts[0];
        if (leafCount > MaxLeafCount)
            return false;

        var wordCount = leafCount <= 128 ? 2 : 4;
        firstLeaves[0] = 0;
        for (var i = 0; i < nodes.Length; i++)
        {
            ref readonly var node = ref nodes[i];
            if (!node.IsLeaf)
            {
                firstLeaves[node.LeftOrContext] = firstLeaves[i];
                firstLeaves[node.RightOrPredictor] = firstLeaves[i] + leafCounts[node.LeftOrContext];
            }
        }

        // The tables of the properties, from the smallest split value to the largest one plus one, every leaf set
        long tableLength = 0;
        for (var slot = 0; slot < propertyCount; slot++)
        {
            tableLength += (long)largest[slot] - smallest[slot] + 2;
            if (tableLength > MaxPropertyTableLength)
                return false;
        }

        var tableOffset = 0;
        for (var slot = 0; slot < propertyCount; slot++)
        {
            var length = largest[slot] - smallest[slot] + 2;
            slotBuffer[slot] = new Slot(properties[slot], smallest[slot], length - 1, tableOffset);
            tableOffset += length * wordCount;
        }

        buffer[..tableOffset].Fill(ulong.MaxValue);

        // A decision is false for the values up to its split value, and rules out the leaves of its left child: the
        // decisions are first recorded at their split value, then the masks are combined from the largest value down
        foreach (ref readonly var node in nodes)
        {
            if (node.IsLeaf)
                continue;

            var slot = slotBuffer[slotOfProperty[node.Property]];
            var mask = buffer.Slice(slot.TableOffset + ((node.ValueOrOffset - slot.Low) * wordCount), wordCount);
            var first = firstLeaves[node.LeftOrContext];
            var last = first + leafCounts[node.LeftOrContext] - 1;
            for (var word = first >> 6; word <= last >> 6; word++)
            {
                // The bits of the leaves of the word, from the first one to the last one
                var low = Math.Max(first, word << 6) & 63;
                var high = Math.Min(last, (word << 6) + 63) & 63;
                mask[word] &= ~(((2UL << (high - low)) - 1) << low);
            }
        }

        for (var slot = 0; slot < propertyCount; slot++)
        {
            var info = slotBuffer[slot];
            var masks = buffer.Slice(info.TableOffset, (info.Last + 1) * wordCount);
            for (var position = masks.Length - (2 * wordCount); position >= 0; position -= wordCount)
            {
                for (var word = 0; word < wordCount; word++)
                {
                    masks[position + word] &= masks[position + wordCount + word];
                }
            }
        }

        // The leaves, with the cluster of their context in the high byte (a tree has fewer than 2^24 nodes, and a stream
        // fewer than 256 clusters)
        var leaves = buffer.Slice(tableOffset, wordCount * 64);
        leaves.Clear();
        for (var i = 0; i < nodes.Length; i++)
        {
            if (nodes[i].IsLeaf)
            {
                leaves[firstLeaves[i]] = (uint)(i | (decoder.GetCluster(nodes[i].LeftOrContext) << 24));
            }
        }

        table = new JpegXlMaTreeTable(buffer[..(tableOffset + leaves.Length)], slotBuffer[..propertyCount], tableOffset, wordCount == 4);
        return true;
    }

    /// <summary>
    /// Returns whether a tree may be a table: a decision at its root, few nodes, and decisions on at most
    /// <see cref="MaxPropertyCount"/> properties of the sample and its neighbors. The leaves and the split values are
    /// counted when the table is built.
    /// </summary>
    public static bool IsCandidate(ReadOnlySpan<JpegXlMaTreeNode> nodes)
    {
        if (nodes.Length > MaxNodeCount || nodes[0].IsLeaf)
            return false;

        var used = 0;
        foreach (ref readonly var node in nodes)
        {
            if (node.IsLeaf)
                continue;

            if (node.Property is < 2 or > JpegXlMaTree.WeightedProperty or 8)
                return false;

            used |= 1 << node.Property;
        }

        return BitOperations.PopCount((uint)used) <= MaxPropertyCount;
    }

    /// <summary>Returns the leaf of a sample, with the cluster of the context of the leaf in the high byte.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetCell(int x, int y, int maxError, long left, long top, long topLeft, long topRight, long leftLeft, long topTop)
    {
        var buffer = _buffer;
        var mask0 = ulong.MaxValue;
        var mask1 = ulong.MaxValue;
        var mask2 = ulong.MaxValue;
        var mask3 = ulong.MaxValue;
        var isWide = _isWide;
        foreach (ref readonly var slot in _slots)
        {
            // The properties are those of the general loop of the Modular decoder; the property of a slot does not change,
            // so the branch of the switch is predictable
            int value = slot.Property switch
            {
                2 => y,
                3 => x,
                4 => (int)Magnitude(top),
                5 => (int)Magnitude(left),
                6 => (int)top,
                7 => (int)left,
                9 => (int)(left + top - topLeft),
                10 => (int)(left - topLeft),
                11 => (int)(topLeft - top),
                12 => (int)(top - topRight),
                13 => (int)(top - topTop),
                14 => (int)(left - leftLeft),
                _ => maxError,
            };

            // The position in the table of the property, clamped to its first and its last entry without a branch: the
            // values are not predictable
            var position = (long)value - slot.Low;
            position &= ~(position >> 63);
            var beyond = slot.Last - position;
            position += beyond & (beyond >> 63);
            if (isWide)
            {
                var mask = slot.TableOffset + ((int)position * 4);
                mask0 &= buffer[mask];
                mask1 &= buffer[mask + 1];
                mask2 &= buffer[mask + 2];
                mask3 &= buffer[mask + 3];
            }
            else
            {
                var mask = slot.TableOffset + ((int)position * 2);
                mask0 &= buffer[mask];
                mask1 &= buffer[mask + 1];
            }
        }

        // The first leaf left: the zeros of the first word that is not zero, plus 64 for each zero word before it
        var zeros = BitOperations.TrailingZeroCount(mask0);
        var count = zeros;
        var isZero = -(zeros >> 6);
        zeros = BitOperations.TrailingZeroCount(mask1);
        count += zeros & isZero;
        if (isWide)
        {
            isZero &= -(zeros >> 6);
            zeros = BitOperations.TrailingZeroCount(mask2);
            count += zeros & isZero;
            isZero &= -(zeros >> 6);
            count += BitOperations.TrailingZeroCount(mask3) & isZero;
        }

        return (int)buffer[_leafOffset + count];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Magnitude(long value) => (value ^ (value >> 63)) - (value >> 63);

    /// <summary>A property of a table: its index, its smallest split value, the last position of its table, and the offset of its table in the buffer.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct Slot(int Property, int Low, int Last, int TableOffset);
}
