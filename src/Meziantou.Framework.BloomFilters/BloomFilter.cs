using System.Runtime.CompilerServices;

namespace Meziantou.Framework.BloomFilters;

public abstract partial class BloomFilter
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
