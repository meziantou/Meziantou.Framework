namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Tag trees (T.800, B.10.2): a quad-tree over a two-dimensional array of non-negative integers, in which a node holds
/// the minimum of its children. A decoder learns the values incrementally: a query asks whether the value of a leaf is
/// below a threshold and reads only the bits that the earlier queries did not.
/// </summary>
/// <remarks>
/// A tree is stored as two integers per node, level by level from the leaves: the lower bound known so far, and the
/// value once it is known (<see cref="UnknownValue"/> before).
/// </remarks>
internal static class Jpeg2000TagTree
{
    /// <summary>The value of a node that is not known yet.</summary>
    public const int UnknownValue = int.MaxValue;

    /// <summary>The largest number of levels: a tree over at most 2^15 by 2^15 leaves.</summary>
    public const int MaxLevels = 17;

    /// <summary>Gets the number of nodes of a tree over <paramref name="width"/> by <paramref name="height"/> leaves.</summary>
    public static long GetNodeCount(int width, int height)
    {
        var count = 0L;
        while (true)
        {
            count += (long)width * height;
            if (width == 1 && height == 1)
                return count;

            width = (width + 1) >> 1;
            height = (height + 1) >> 1;
        }
    }

    /// <summary>Initializes the nodes of a tree: nothing is known.</summary>
    /// <param name="nodes">The nodes of the tree: two integers each.</param>
    public static void Reset(Span<int> nodes)
    {
        for (var i = 0; i < nodes.Length; i += 2)
        {
            nodes[i] = 0;
            nodes[i + 1] = UnknownValue;
        }
    }

    /// <summary>Determines whether the value of a leaf is less than a threshold, reading the bits that are needed for it.</summary>
    /// <param name="nodes">The nodes of the tree: two integers each.</param>
    /// <param name="width">The number of leaves in a row.</param>
    /// <param name="height">The number of rows of leaves.</param>
    /// <param name="x">The column of the leaf.</param>
    /// <param name="y">The row of the leaf.</param>
    /// <param name="threshold">The threshold.</param>
    /// <param name="reader">The reader of the packet header.</param>
    /// <returns><see langword="true"/> if the value of the leaf is known and less than <paramref name="threshold"/>.</returns>
    public static bool IsLessThan(Span<int> nodes, int width, int height, int x, int y, int threshold, ref Jpeg2000PacketBitReader reader)
    {
        // The path from the leaf to the root
        Span<int> path = stackalloc int[MaxLevels];
        var levels = 0;
        var offset = 0;
        while (true)
        {
            path[levels++] = offset + (y * width) + x;
            if (width == 1 && height == 1)
                break;

            offset += width * height;
            width = (width + 1) >> 1;
            height = (height + 1) >> 1;
            x >>= 1;
            y >>= 1;
        }

        // From the root: a node is at least its parent; a 0 bit raises its lower bound, a 1 bit says the bound is its value
        var low = 0;
        var value = UnknownValue;
        for (var level = levels - 1; level >= 0; level--)
        {
            var node = 2 * path[level];
            if (nodes[node] > low)
            {
                low = nodes[node];
            }

            value = nodes[node + 1];
            while (low < threshold && low < value)
            {
                if (reader.ReadBit() != 0)
                {
                    value = low;
                    nodes[node + 1] = value;
                }
                else
                {
                    low++;
                }
            }

            nodes[node] = low;
        }

        return value < threshold;
    }
}
