namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes the tag trees of packet headers (T.800, B.10.2), the reverse of <see cref="Jpeg2000TagTree"/>: the values of the
/// leaves are given, each node gets the minimum of its children, and a leaf is coded up to a threshold.
/// </summary>
/// <remarks>
/// A tree is three arrays of its nodes, level by level from the leaves, that the caller keeps from one packet to the next:
/// the values, the lower bound written so far, and whether the value is written (0 or 1). The bits written for a threshold
/// only depend on the values through their minimum with the threshold, so the leaves whose value is not known yet can hold
/// any value at least as large.
/// </remarks>
internal static class Jpeg2000TagTreeEncoder
{
    /// <summary>Computes the nodes above the leaves: each one is the minimum of its children.</summary>
    /// <param name="values">The values of the nodes; the leaves come first, row by row.</param>
    /// <param name="width">The number of leaves in a row.</param>
    /// <param name="height">The number of rows of leaves.</param>
    public static void ComputeMinima(Span<int> values, int width, int height)
    {
        var offset = 0;
        while (width != 1 || height != 1)
        {
            var parentWidth = (width + 1) >> 1;
            var parentHeight = (height + 1) >> 1;
            var parentOffset = offset + (width * height);
            values.Slice(parentOffset, parentWidth * parentHeight).Fill(int.MaxValue);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    ref var parent = ref values[parentOffset + ((y >> 1) * parentWidth) + (x >> 1)];
                    parent = Math.Min(parent, values[offset + (y * width) + x]);
                }
            }

            offset = parentOffset;
            width = parentWidth;
            height = parentHeight;
        }
    }

    /// <summary>Writes what tells a decoder whether the value of a leaf is less than a threshold, and the value itself when it is.</summary>
    /// <param name="writer">The writer of the packet header.</param>
    /// <param name="values">The values of the nodes.</param>
    /// <param name="lows">The lower bound of each node written so far.</param>
    /// <param name="known">Whether the value of each node is written.</param>
    /// <param name="width">The number of leaves in a row.</param>
    /// <param name="height">The number of rows of leaves.</param>
    /// <param name="x">The column of the leaf.</param>
    /// <param name="y">The row of the leaf.</param>
    /// <param name="threshold">The threshold.</param>
    public static void Encode(ref Jpeg2000PacketBitWriter writer, ReadOnlySpan<int> values, Span<int> lows, Span<int> known, int width, int height, int x, int y, int threshold)
    {
        Span<int> path = stackalloc int[Jpeg2000TagTree.MaxLevels];
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

        var low = 0;
        for (var level = levels - 1; level >= 0; level--)
        {
            var node = path[level];
            if (lows[node] > low)
            {
                low = lows[node];
            }

            var value = values[node];
            while (low < threshold)
            {
                if (low >= value)
                {
                    if (known[node] == 0)
                    {
                        writer.WriteBit(1);
                        known[node] = 1;
                    }

                    break;
                }

                writer.WriteBit(0);
                low++;
            }

            lows[node] = low;
        }
    }
}
