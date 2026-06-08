
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.BloomFilters;

public abstract partial class BloomFilter
{
    private protected readonly SegmentedBitStorage Bits;
    private protected readonly int HashCount;

    protected BloomFilter(long bitCount, int hashCount)
    {
        Bits = new SegmentedBitStorage(bitCount);
        HashCount = hashCount;
    }
}

internal readonly struct Hash128
{
    public readonly ulong Hash1;
    public readonly ulong Hash2;

    public Hash128(ulong low, ulong high)
    {
        Hash1 = low;
        Hash2 = high == 0 ? 0x9E3779B97F4A7C15UL : high; // Golden ratio
    }

    public Hash128(UInt128 value)
    {
        Hash1 = (ulong)value;
        Hash2 = (ulong)(value >> 64);
    }
}


public sealed partial class BloomFilterXXHash128 : BloomFilter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddHash(Hash128 hash)
    {
        var bitCount = (ulong)Bits.BitCount;
        for (var i = 0; i < HashCount; i++)
        {
            var combined = hash.Hash1 + ((ulong)i * hash.Hash2);
            var index = Reduce(combined, bitCount);
            Bits.Set(index);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool MayContainHash(Hash128 hash)
    {
        var bitCount = (ulong)Bits.BitCount;
        for (var i = 0; i < HashCount; i++)
        {
            var combined = hash.Hash1 + ((ulong)i * hash.Hash2);
            var index = Reduce(combined, bitCount);
            if (!Bits.IsSet(index))
                return false;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Hash128 Hash<T>(in T value) where T : unmanaged
    {
        var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in value), 1));
        return new(XxHash128.HashToUInt128(bytes));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Hash128 Hash(string value)
    {
        var chars = value.AsSpan();
        var bytes = MemoryMarshal.AsBytes(chars);
        return new(XxHash128.HashToUInt128(bytes));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Reduce(ulong hash, ulong range) => (long)(((UInt128)hash * range) >> 64);
}
