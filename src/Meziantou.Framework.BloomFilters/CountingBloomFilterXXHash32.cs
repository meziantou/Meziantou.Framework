using System.IO.Hashing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;

namespace Meziantou.Framework.BloomFilters;

public sealed partial class CountingBloomFilterXXHash32 : CountingBloomFilter
{
    private const uint Prime2 = 0x85EBCA77U;
    private const uint Prime3 = 0xC2B2AE3DU;
    private const uint Prime4 = 0x27D4EB2FU;
    private const uint Prime5 = 0x165667B1U;

    private void AddRangeCore(ReadOnlySpan<int> values)
    {
        AddRangeCore(unsafe(MemoryMarshal.Cast<int, uint>(values)));
    }

    private void RemoveRangeCore(ReadOnlySpan<int> values)
    {
        RemoveRangeCore(unsafe(MemoryMarshal.Cast<int, uint>(values)));
    }

    private void AddRangeCore(ReadOnlySpan<uint> values)
    {
        UpdateRangeCore(values, remove: false);
    }

    private void RemoveRangeCore(ReadOnlySpan<uint> values)
    {
        UpdateRangeCore(values, remove: true);
    }

    private void UpdateRangeCore(ReadOnlySpan<uint> values, bool remove)
    {
        if (!BitConverter.IsLittleEndian)
        {
            foreach (var value in values)
            {
                UpdateHash(Hash(value), remove);
            }

            return;
        }

        var index = 0;

        // On Arm64, the vector path is slower than the scalar hash (measured on .NET 10): the multiplications are
        // cheap, but each lane must then be moved back to a general-purpose register to update the filter.
        if (Vector.IsHardwareAccelerated && !AdvSimd.IsSupported && values.Length >= Vector<uint>.Count)
        {
            var hash = new Vector<uint>(Prime5 + sizeof(uint));
            var prime2 = new Vector<uint>(Prime2);
            var prime3 = new Vector<uint>(Prime3);
            var prime4 = new Vector<uint>(Prime4);
            while (index <= values.Length - Vector<uint>.Count)
            {
                var current = Vector.Create(values[index..]);
                var hashes = hash + (current * prime3);
                hashes = RotateLeft(hashes, 17) * prime4;
                hashes ^= hashes >> 15;
                hashes *= prime2;
                hashes ^= hashes >> 13;
                hashes *= prime3;
                hashes ^= hashes >> 16;

                for (var lane = 0; lane < Vector<uint>.Count; lane++)
                {
                    UpdateHash(BloomFilterHash.FromUInt32(hashes[lane]), remove);
                }

                index += Vector<uint>.Count;
            }
        }

        foreach (var value in values[index..])
        {
            UpdateHash(BloomFilterHash.FromUInt32(HashUInt32(value)), remove);
        }
    }

    /// <summary>Computes <see cref="XxHash32"/> of the little-endian bytes of <paramref name="value"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint HashUInt32(uint value)
    {
        var hash = BitOperations.RotateLeft(Prime5 + sizeof(uint) + (value * Prime3), 17) * Prime4;
        hash ^= hash >> 15;
        hash *= Prime2;
        hash ^= hash >> 13;
        hash *= Prime3;
        hash ^= hash >> 16;
        return hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateHash(BloomFilterHash hash, bool remove)
    {
        if (remove)
        {
            RemoveHash(hash);
        }
        else
        {
            AddHash(hash);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static BloomFilterHash Hash(ReadOnlySpan<byte> value)
    {
        return BloomFilterHash.FromUInt32(XxHash32.HashToUInt32(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<uint> RotateLeft(Vector<uint> value, int offset) => (value << offset) | (value >> (32 - offset));
}
