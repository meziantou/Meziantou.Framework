using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads a permutation (ISO/IEC 18181-1, section C.3.2 for the table of contents and I.3 for the coefficient orders): an
/// entropy-coded Lehmer code, in which each value is the rank of an element among those not yet used.
/// </summary>
internal static class JpegXlPermutation
{
    /// <summary>The number of contexts of the stream of a permutation.</summary>
    public const int ContextCount = 8;

    /// <summary>Reads a permutation that has its own entropy-coded stream.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="size">The number of elements.</param>
    /// <param name="skip">The number of leading elements that keep their position.</param>
    /// <param name="scope">The scope the tables of the stream are rented from.</param>
    /// <returns>The permutation.</returns>
    public static int[] Read(JpegXlBitReader reader, int size, int skip, AllocationScope scope)
    {
        using var code = JpegXlEntropyCode.Read(reader, ContextCount, scope);
        using var decoder = new JpegXlEntropyDecoder(code, scope);
        decoder.Begin(reader);
        var permutation = new int[size];
        Read(reader, decoder, permutation, skip, scope);
        decoder.EnsureFinalState();
        return permutation;
    }

    /// <summary>Reads a permutation from a stream that is already started.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="decoder">The decoder of the stream, of <see cref="ContextCount"/> contexts.</param>
    /// <param name="permutation">Receives the permutation; its length is the number of elements.</param>
    /// <param name="skip">The number of leading elements that keep their position.</param>
    /// <param name="scope">The scope of the working memory.</param>
    public static void Read(JpegXlBitReader reader, JpegXlEntropyDecoder decoder, Span<int> permutation, int skip, AllocationScope scope)
    {
        var size = permutation.Length;
        var end = (long)decoder.Read(reader, GetContext((uint)size)) + skip;
        if (end > size)
            throw JpegXlFormat.Invalid("a permutation is longer than what it permutes.");

        // The Lehmer code, then the element of each rank among the unused ones: a Fenwick tree counts them
        using var work = Av1PooledArray<int>.Rent(scope, (2L * size) + 1, clear: true);
        var lehmer = work.Span[..size];
        uint last = 0;
        for (var i = skip; i < end; i++)
        {
            last = decoder.Read(reader, GetContext(last));
            if (last >= size - i)
                throw JpegXlFormat.Invalid("a permutation names an element that is already used.");

            lehmer[i] = (int)last;
            if ((i & 0xFF) == 0xFF && reader.IsOverrun)
                throw JpegXlFormat.Invalid("a permutation is truncated.");
        }

        var tree = work.Span[size..];
        for (var i = 1; i <= size; i++)
        {
            tree[i] = i & -i;
        }

        var highestBit = size == 0 ? 0 : 1 << BitOperations.Log2((uint)size);
        for (var i = 0; i < size; i++)
        {
            // Find the element that has lehmer[i] unused elements before it
            var rank = lehmer[i];
            var position = 0;
            for (var step = highestBit; step > 0; step >>= 1)
            {
                var next = position + step;
                if (next <= size && tree[next] <= rank)
                {
                    position = next;
                    rank -= tree[next];
                }
            }

            permutation[i] = position;
            for (var j = position + 1; j <= size; j += j & -j)
            {
                tree[j]--;
            }
        }
    }

    private static int GetContext(uint value) => value == 0 ? 0 : Math.Min(BitOperations.Log2(value) + 1, ContextCount - 1);
}
