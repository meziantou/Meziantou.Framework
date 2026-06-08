
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

internal readonly struct Hash64
{
    public readonly uint Hash1;
    public readonly uint Hash2;

    public Hash64(ulong value)
    {
        Hash1 = (uint)value;
        Hash2 = (uint)(value >> 32);
        if (Hash2 == 0)
            Hash2 = 0x9E3779B9U;
    }
}

internal readonly struct Hash32
{
    public readonly ushort Hash1;
    public readonly ushort Hash2;

    public Hash32(uint value)
    {
        Hash1 = (ushort)value;
        Hash2 = (ushort)(value >> 16);
        if (Hash2 == 0)
            Hash2 = 0x9E37;
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

public sealed partial class BloomFilterXXHash64 : BloomFilter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddHash(Hash64 hash)
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
    private bool MayContainHash(Hash64 hash)
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
    private static Hash64 Hash<T>(in T value) where T : unmanaged
    {
        var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in value), 1));
        var hash = XxHash64.HashToUInt64(bytes);
        return new(hash);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Hash64 Hash(string value)
    {
        var chars = value.AsSpan();
        var bytes = MemoryMarshal.AsBytes(chars);
        var hash = XxHash64.HashToUInt64(bytes);
        return new(hash);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Reduce(ulong hash, ulong range) => (long)(((UInt128)hash * range) >> 64);
}

public sealed partial class BloomFilterXXHash32 : BloomFilter
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddHash(Hash32 hash)
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
    private bool MayContainHash(Hash32 hash)
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
    private static Hash32 Hash<T>(in T value) where T : unmanaged
    {
        var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in value), 1));
        var hash = XxHash32.HashToUInt32(bytes);
        return new(hash);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Hash32 Hash(string value)
    {
        var chars = value.AsSpan();
        var bytes = MemoryMarshal.AsBytes(chars);
        var hash = XxHash32.HashToUInt32(bytes);
        return new(hash);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Reduce(ulong hash, ulong range) => (long)(((UInt128)hash * range) >> 64);
}
