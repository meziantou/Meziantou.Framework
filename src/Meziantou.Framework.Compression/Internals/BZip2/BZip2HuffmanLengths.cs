namespace Meziantou.Framework.Compression.Internals.BZip2;

/// <summary>Computes the lengths of the Huffman codes of the symbols of a bzip2 block, limited to a maximum length.</summary>
internal static class BZip2HuffmanLengths
{
    /// <summary>
    /// Computes the lengths of a Huffman code where every symbol has a code (a symbol that does not appear counts once),
    /// as bzip2 does: the ties are broken by the depth of the subtrees, and the frequencies are halved until no code is
    /// longer than <paramref name="maxLength"/>.
    /// </summary>
    public static void Compute(ReadOnlySpan<int> frequencies, Span<byte> lengths, int maxLength)
    {
        var count = frequencies.Length;
        Span<long> weights = stackalloc long[count * 2];
        Span<int> parents = stackalloc int[count * 2];
        Span<int> heap = stackalloc int[count + 1];
        Span<long> scaled = stackalloc long[count];
        for (var i = 0; i < count; i++)
        {
            scaled[i] = Math.Max(frequencies[i], 1);
        }

        while (true)
        {
            // The weight is the frequency, shifted, and the depth of the subtree in the lowest 8 bits
            for (var i = 0; i < count; i++)
            {
                weights[i] = scaled[i] << 8;
            }

            var heapLength = 0;
            for (var i = 0; i < count; i++)
            {
                Push(heap, ref heapLength, weights, i);
            }

            var nodes = count;
            while (heapLength > 1)
            {
                var first = Pop(heap, ref heapLength, weights);
                var second = Pop(heap, ref heapLength, weights);
                parents[first] = nodes;
                parents[second] = nodes;
                weights[nodes] = ((weights[first] & ~0xFFL) + (weights[second] & ~0xFFL)) | (1 + Math.Max(weights[first] & 0xFF, weights[second] & 0xFF));
                Push(heap, ref heapLength, weights, nodes);
                nodes++;
            }

            var root = nodes - 1;
            parents[root] = -1;
            var tooLong = false;
            for (var i = 0; i < count; i++)
            {
                var length = 0;
                for (var node = i; node != root; node = parents[node])
                {
                    length++;
                }

                // A single symbol has a code of one bit
                length = Math.Max(length, 1);
                lengths[i] = (byte)Math.Min(length, 255);
                tooLong |= length > maxLength;
            }

            if (!tooLong)
                return;

            for (var i = 0; i < count; i++)
            {
                scaled[i] = 1 + (scaled[i] / 2);
            }
        }
    }

    private static void Push(Span<int> heap, ref int length, Span<long> weights, int node)
    {
        var index = length++;
        while (index > 0)
        {
            var parent = (index - 1) / 2;
            if (weights[heap[parent]] <= weights[node])
                break;

            heap[index] = heap[parent];
            index = parent;
        }

        heap[index] = node;
    }

    private static int Pop(Span<int> heap, ref int length, Span<long> weights)
    {
        var result = heap[0];
        var last = heap[--length];
        var index = 0;
        while (true)
        {
            var child = (2 * index) + 1;
            if (child >= length)
                break;

            if (child + 1 < length && weights[heap[child + 1]] < weights[heap[child]])
            {
                child++;
            }

            if (weights[heap[child]] >= weights[last])
                break;

            heap[index] = heap[child];
            index = child;
        }

        heap[index] = last;
        return result;
    }
}
