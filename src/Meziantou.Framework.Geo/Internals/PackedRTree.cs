using System.Buffers.Binary;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>
/// The static packed Hilbert R-tree of FlatGeobuf: nodes of 40 bytes (the bounds as four doubles, then a 64-bit offset),
/// the root first and the leaves last. The offset of a leaf is the position of its feature in the data, and the offset
/// of another node the index of its first child.
/// </summary>
internal static class PackedRTree
{
    public const int NodeSize = 40;

    private const uint HilbertMax = (1u << 16) - 1;

    /// <summary>Gets the size of the tree of a number of features, in bytes.</summary>
    public static long GetSize(ulong count, ushort nodeSize)
    {
        if (nodeSize < 2)
            throw new FormatException("The node size of the index of the FlatGeobuf data must be at least 2.");

        if (count > 1UL << 56)
            throw new FormatException("The FlatGeobuf data declares too many features.");

        return checked(GetLevelBounds(count, nodeSize)[0].End * NodeSize);
    }

    /// <summary>Orders boxes along a Hilbert curve, from the largest value to the smallest one, as the reference implementation does; the order of the boxes of the same value is kept.</summary>
    public static int[] GetHilbertOrder(IReadOnlyList<GeoBounds> boxes)
    {
        var extent = boxes[0];
        foreach (var box in boxes)
        {
            extent = extent.Union(box);
        }

        var width = extent.East - extent.West;
        var height = extent.North - extent.South;
        var values = new uint[boxes.Count];
        for (var i = 0; i < boxes.Count; i++)
        {
            var box = boxes[i];
            var x = width == 0 ? 0 : (uint)Math.Floor(HilbertMax * (((box.West + box.East) / 2) - extent.West) / width);
            var y = height == 0 ? 0 : (uint)Math.Floor(HilbertMax * (((box.South + box.North) / 2) - extent.South) / height);
            values[i] = Hilbert(x, y);
        }

        return [.. Enumerable.Range(0, boxes.Count).OrderByDescending(i => values[i])];
    }

    /// <summary>Writes the tree of boxes, in the order of the features, whose features are at the given positions of the data.</summary>
    public static byte[] Write(IReadOnlyList<GeoBounds> boxes, IReadOnlyList<long> offsets, ushort nodeSize)
    {
        var levels = GetLevelBounds((ulong)boxes.Count, nodeSize);
        var count = (int)levels[0].End;
        var nodes = new (GeoBounds Box, long Offset)[count];
        var leaves = count - boxes.Count;
        for (var i = 0; i < boxes.Count; i++)
        {
            nodes[leaves + i] = (boxes[i], offsets[i]);
        }

        // Each level from the leaves: a node covers up to nodeSize nodes of the level below
        for (var level = 0; level < levels.Length - 1; level++)
        {
            var position = (int)levels[level].Start;
            var end = (int)levels[level].End;
            var parent = (int)levels[level + 1].Start;
            while (position < end)
            {
                var first = position;
                var box = nodes[position].Box;
                for (var j = 0; j < nodeSize && position < end; j++)
                {
                    box = box.Union(nodes[position++].Box);
                }

                nodes[parent++] = (box, first);
            }
        }

        var result = new byte[count * NodeSize];
        for (var i = 0; i < count; i++)
        {
            var span = result.AsSpan(i * NodeSize);
            BinaryPrimitives.WriteDoubleLittleEndian(span, nodes[i].Box.West);
            BinaryPrimitives.WriteDoubleLittleEndian(span[8..], nodes[i].Box.South);
            BinaryPrimitives.WriteDoubleLittleEndian(span[16..], nodes[i].Box.East);
            BinaryPrimitives.WriteDoubleLittleEndian(span[24..], nodes[i].Box.North);
            BinaryPrimitives.WriteInt64LittleEndian(span[32..], nodes[i].Offset);
        }

        return result;
    }

    /// <summary>Finds the features whose boxes intersect a box, and returns their positions in the data, in increasing order.</summary>
    public static List<long> Search(ReadOnlySpan<byte> tree, ulong count, ushort nodeSize, GeoBounds bounds)
    {
        var levels = GetLevelBounds(count, nodeSize);
        var nodeCount = levels[0].End;
        var leaves = nodeCount - (long)count;
        if (tree.Length < nodeCount * NodeSize)
            throw new FormatException("The index of the FlatGeobuf data is truncated.");

        var results = new List<long>();
        var queue = new Stack<(long Node, int Level)>();
        queue.Push((0, levels.Length - 1));
        while (queue.Count > 0)
        {
            var (node, level) = queue.Pop();

            // An offset that comes from the file must be a node of its level
            if (node < levels[level].Start || node >= levels[level].End)
                throw new FormatException("The index of the FlatGeobuf data is not valid.");

            var end = Math.Min(node + nodeSize, levels[level].End);
            for (var position = node; position < end; position++)
            {
                var span = tree[(int)(position * NodeSize)..];
                var west = BinaryPrimitives.ReadDoubleLittleEndian(span);
                var south = BinaryPrimitives.ReadDoubleLittleEndian(span[8..]);
                var east = BinaryPrimitives.ReadDoubleLittleEndian(span[16..]);
                var north = BinaryPrimitives.ReadDoubleLittleEndian(span[24..]);
                if (west > bounds.East || east < bounds.West || south > bounds.North || north < bounds.South)
                    continue;

                var offset = BinaryPrimitives.ReadInt64LittleEndian(span[32..]);
                if (position >= leaves)
                {
                    results.Add(offset);
                }
                else if (level > 0)
                {
                    queue.Push((offset, level - 1));
                }
            }
        }

        results.Sort();
        return results;
    }

    /// <summary>Gets the range of the nodes of each level, from the leaves to the root, in the order of the storage, where the root is first.</summary>
    private static (long Start, long End)[] GetLevelBounds(ulong count, ushort nodeSize)
    {
        var levelCounts = new List<long>();
        var n = (long)count;
        var total = n;
        levelCounts.Add(n);
        do
        {
            n = (n + nodeSize - 1) / nodeSize;
            total += n;
            levelCounts.Add(n);
        }
        while (n != 1);

        var result = new (long Start, long End)[levelCounts.Count];
        var offset = total;
        for (var i = 0; i < levelCounts.Count; i++)
        {
            offset -= levelCounts[i];
            result[i] = (offset, offset + levelCounts[i]);
        }

        return result;
    }

    // From the public domain code of https://github.com/rawrunprotected/hilbert_curves, as the reference implementation uses it
    private static uint Hilbert(uint x, uint y)
    {
        var a = x ^ y;
        var b = 0xFFFF ^ a;
        var c = 0xFFFF ^ (x | y);
        var d = x & (y ^ 0xFFFF);

        var a1 = a | (b >> 1);
        var b1 = (a >> 1) ^ a;
        var c1 = ((c >> 1) ^ (b & (d >> 1))) ^ c;
        var d1 = ((a & (c >> 1)) ^ (d >> 1)) ^ d;

        a = a1;
        b = b1;
        c = c1;
        d = d1;
        a1 = (a & (a >> 2)) ^ (b & (b >> 2));
        b1 = (a & (b >> 2)) ^ (b & ((a ^ b) >> 2));
        c1 ^= (a & (c >> 2)) ^ (b & (d >> 2));
        d1 ^= (b & (c >> 2)) ^ ((a ^ b) & (d >> 2));

        a = a1;
        b = b1;
        c = c1;
        d = d1;
        a1 = (a & (a >> 4)) ^ (b & (b >> 4));
        b1 = (a & (b >> 4)) ^ (b & ((a ^ b) >> 4));
        c1 ^= (a & (c >> 4)) ^ (b & (d >> 4));
        d1 ^= (b & (c >> 4)) ^ ((a ^ b) & (d >> 4));

        a = a1;
        b = b1;
        c = c1;
        d = d1;
        c1 ^= (a & (c >> 8)) ^ (b & (d >> 8));
        d1 ^= (b & (c >> 8)) ^ ((a ^ b) & (d >> 8));

        a = c1 ^ (c1 >> 1);
        b = d1 ^ (d1 >> 1);

        var i0 = x ^ y;
        var i1 = b | (0xFFFF ^ (i0 | a));

        i0 = (i0 | (i0 << 8)) & 0x00FF00FF;
        i0 = (i0 | (i0 << 4)) & 0x0F0F0F0F;
        i0 = (i0 | (i0 << 2)) & 0x33333333;
        i0 = (i0 | (i0 << 1)) & 0x55555555;

        i1 = (i1 | (i1 << 8)) & 0x00FF00FF;
        i1 = (i1 | (i1 << 4)) & 0x0F0F0F0F;
        i1 = (i1 | (i1 << 2)) & 0x33333333;
        i1 = (i1 | (i1 << 1)) & 0x55555555;

        return (i1 << 1) | i0;
    }
}
