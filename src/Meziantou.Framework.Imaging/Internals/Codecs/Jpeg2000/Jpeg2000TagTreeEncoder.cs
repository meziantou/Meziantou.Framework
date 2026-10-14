namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes the tag trees of a packet header (T.800, B.10.2), the reverse of <see cref="Jpeg2000TagTree"/>: the values of
/// the leaves are given, each node gets the minimum of its children, and a leaf is coded up to a threshold.
/// </summary>
internal sealed class Jpeg2000TagTreeEncoder : IDisposable
{
    private const int FieldsPerNode = 3;

    private readonly AllocationScope _scope;
    private Av1PooledArray<int>? _nodes;
    private int _width;
    private int _height;

    public Jpeg2000TagTreeEncoder(AllocationScope scope)
    {
        _scope = scope;
    }

    /// <summary>Starts a tree and returns its leaves, row by row, for the caller to fill.</summary>
    public Span<int> Begin(int width, int height)
    {
        _width = width;
        _height = height;
        var count = Jpeg2000TagTree.GetNodeCount(width, height);
        if (_nodes is null || _nodes.Length < count * FieldsPerNode)
        {
            _nodes?.Dispose();
            _nodes = null;
            _nodes = Av1PooledArray<int>.Rent(_scope, count * FieldsPerNode, clear: false, AllocationKind.Temporary);
        }

        // The values of the nodes, level by level from the leaves; then, for each node, the lower bound that was written
        // and whether its value was written
        _nodes.Span.Slice((int)count, (int)count * 2).Clear();
        return _nodes.Span[..(width * height)];
    }

    /// <summary>Computes the nodes above the leaves: each one is the minimum of its children.</summary>
    public void Complete()
    {
        var nodes = _nodes!.Span;
        var width = _width;
        var height = _height;
        var offset = 0;
        while (width != 1 || height != 1)
        {
            var parentWidth = (width + 1) >> 1;
            var parentHeight = (height + 1) >> 1;
            var parentOffset = offset + (width * height);
            nodes.Slice(parentOffset, parentWidth * parentHeight).Fill(int.MaxValue);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    ref var parent = ref nodes[parentOffset + ((y >> 1) * parentWidth) + (x >> 1)];
                    parent = Math.Min(parent, nodes[offset + (y * width) + x]);
                }
            }

            offset = parentOffset;
            width = parentWidth;
            height = parentHeight;
        }
    }

    /// <summary>Writes what tells a decoder whether the value of a leaf is less than a threshold, and the value itself when it is.</summary>
    public void Encode(ref Jpeg2000PacketBitWriter writer, int x, int y, int threshold)
    {
        var nodes = _nodes!.Span;
        var count = (int)Jpeg2000TagTree.GetNodeCount(_width, _height);
        Span<int> path = stackalloc int[Jpeg2000TagTree.MaxLevels];
        var levels = 0;
        var width = _width;
        var height = _height;
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
            ref var written = ref nodes[count + node];
            ref var isKnown = ref nodes[(2 * count) + node];
            if (written > low)
            {
                low = written;
            }

            var value = nodes[node];
            while (low < threshold)
            {
                if (low >= value)
                {
                    if (isKnown == 0)
                    {
                        writer.WriteBit(1);
                        isKnown = 1;
                    }

                    break;
                }

                writer.WriteBit(0);
                low++;
            }

            written = low;
        }
    }

    public void Dispose()
    {
        _nodes?.Dispose();
        _nodes = null;
    }
}
