using System.Runtime.CompilerServices;

namespace Meziantou.Framework.BloomFilters;

public closed partial class BloomFilter
{
    private protected readonly SegmentedBitStorage Bits;
    private protected readonly int HashCount;

    private protected BloomFilter(long bitCount, int hashCount)
    {
        Bits = new SegmentedBitStorage(bitCount);
        HashCount = hashCount;
    }

    public double GetEstimateCount()
    {
        var setBitCount = Bits.CountSetBits();
        if (setBitCount == Bits.BitCount)
            return double.PositiveInfinity;

        var setBitRatio = (double)setBitCount / Bits.BitCount;
        return -Bits.BitCount / (double)HashCount * Math.Log(1 - setBitRatio);
    }

    /// <summary>
    /// Adds to this filter every value represented by <paramref name="other"/>, by combining their bits with a bitwise OR.
    /// </summary>
    /// <param name="other">
    /// The filter whose values are added. It must use the same hash algorithm (the same concrete type), the same number of
    /// bits and the same number of hash functions as this filter. It is not modified.
    /// </param>
    /// <remarks>
    /// <para>
    /// After the call, <c>MayContain</c> returns <see langword="true"/> for every value added to this filter or to
    /// <paramref name="other"/> before the call: the union never introduces a false negative. Calling the method with the
    /// filter itself does nothing.
    /// </para>
    /// <para>
    /// The result holds the values of both filters in the same number of bits, so more bits are set than in either filter.
    /// Its false positive probability is higher than the one the filters were sized for when the total number of values
    /// exceeds the expected item count. Size each filter for the total number of values when you plan to merge them.
    /// </para>
    /// <para>
    /// The compatibility checks happen before any bit is written: when they fail, this filter is left unchanged.
    /// </para>
    /// <para>
    /// The method can run while other threads call <c>Add</c> or <c>MayContain</c> on either filter. The bits of this filter
    /// are set with atomic operations, so no value added concurrently to this filter is lost. The source is read one 64-bit
    /// word at a time, not as a snapshot: a value added to <paramref name="other"/> during the call may or may not be
    /// included, and a <c>MayContain</c> on this filter during the call may return <see langword="false"/> for a value of
    /// <paramref name="other"/> that is not merged yet. Once the method has returned, every value that was in
    /// <paramref name="other"/> when it was called is included.
    /// </para>
    /// <para>
    /// Counting Bloom filters do not support this operation.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="other"/> uses another hash algorithm, number of bits or number of hash functions.
    /// </exception>
    public void UnionWith(BloomFilter other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(this, other))
            return;

        if (other.GetType() != GetType())
            throw new ArgumentException($"Cannot combine a {GetType().Name} with a {other.GetType().Name}: the filters must use the same hash algorithm.", nameof(other));

        if (other.Bits.BitCount != Bits.BitCount)
            throw new ArgumentException($"Cannot combine a filter of {Bits.BitCount} bits with a filter of {other.Bits.BitCount} bits: the filters must have the same number of bits.", nameof(other));

        if (other.HashCount != HashCount)
            throw new ArgumentException($"Cannot combine a filter that uses {HashCount} hash functions with a filter that uses {other.HashCount}: the filters must use the same number of hash functions.", nameof(other));

        Bits.UnionWith(other.Bits);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected void AddHash(BloomFilterHash hash)
    {
        var bitCount = (ulong)Bits.BitCount;
        var combined = hash.Hash1;
        for (var i = 0; i < HashCount; i++)
        {
            Bits.Set(BloomFilterHash.Reduce(combined, bitCount));
            combined += hash.Hash2;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool MayContainHash(BloomFilterHash hash)
    {
        var bitCount = (ulong)Bits.BitCount;
        var combined = hash.Hash1;
        for (var i = 0; i < HashCount; i++)
        {
            if (!Bits.IsSet(BloomFilterHash.Reduce(combined, bitCount)))
                return false;

            combined += hash.Hash2;
        }

        return true;
    }
}
