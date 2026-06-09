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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected void AddHash(Hash128 hash)
    {
        var bitCount = (ulong)Bits.BitCount;
        var combined = hash.Hash1;
        for (var i = 0; i < HashCount; i++)
        {
            Bits.Set(Reduce(combined, bitCount));
            combined += hash.Hash2;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool MayContainHash(Hash128 hash)
    {
        var bitCount = (ulong)Bits.BitCount;
        var combined = hash.Hash1;
        for (var i = 0; i < HashCount; i++)
        {
            if (!Bits.IsSet(Reduce(combined, bitCount)))
                return false;
            combined += hash.Hash2;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected void AddHash(Hash64 hash)
    {
        var bitCount = (ulong)Bits.BitCount;
        for (var i = 0; i < HashCount; i++)
        {
            var combined = unchecked(hash.Hash1 + ((uint)i * hash.Hash2));
            var index = Reduce(combined, bitCount);
            Bits.Set(index);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool MayContainHash(Hash64 hash)
    {
        var bitCount = (ulong)Bits.BitCount;
        for (var i = 0; i < HashCount; i++)
        {
            var combined = unchecked(hash.Hash1 + ((uint)i * hash.Hash2));
            var index = Reduce(combined, bitCount);
            if (!Bits.IsSet(index))
                return false;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected void AddHash(Hash32 hash)
    {
        var bitCount = (ulong)Bits.BitCount;
        for (var i = 0; i < HashCount; i++)
        {
            var combined = unchecked((ushort)(hash.Hash1 + ((ushort)i * hash.Hash2)));
            var index = Reduce(combined, bitCount);
            Bits.Set(index);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool MayContainHash(Hash32 hash)
    {
        var bitCount = (ulong)Bits.BitCount;
        for (var i = 0; i < HashCount; i++)
        {
            var combined = unchecked((ushort)(hash.Hash1 + ((ushort)i * hash.Hash2)));
            var index = Reduce(combined, bitCount);
            if (!Bits.IsSet(index))
                return false;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Reduce(ulong hash, ulong range) => (long)(((UInt128)hash * range) >> 64);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Reduce(uint hash, ulong range) => (long)(((UInt128)hash * range) >> 32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Reduce(ushort hash, ulong range) => (long)(((UInt128)hash * range) >> 16);
}
