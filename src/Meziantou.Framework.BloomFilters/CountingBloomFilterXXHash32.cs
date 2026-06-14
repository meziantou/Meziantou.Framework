using System.IO.Hashing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.BloomFilters;

public sealed partial class CountingBloomFilterXXHash32 : CountingBloomFilter
{
    private const uint Prime2 = 0x85EBCA77U;
    private const uint Prime3 = 0xC2B2AE3DU;
    private const uint Prime4 = 0x27D4EB2FU;
    private const uint Prime5 = 0x165667B1U;

    private void AddRangeCore(ReadOnlySpan<int> values)
    {
        AddRangeCore(MemoryMarshal.Cast<int, uint>(values));
    }

    private void RemoveRangeCore(ReadOnlySpan<int> values)
    {
        RemoveRangeCore(MemoryMarshal.Cast<int, uint>(values));
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
        if (!BitConverter.IsLittleEndian || !Vector.IsHardwareAccelerated || values.Length < Vector<uint>.Count)
        {
            foreach (var value in values)
            {
                UpdateHash(Hash(value), remove);
            }

            return;
        }

        ref var valuesReference = ref MemoryMarshal.GetReference(values);
        var index = 0;
        var hash = new Vector<uint>(Prime5 + sizeof(uint));
        var prime2 = new Vector<uint>(Prime2);
        var prime3 = new Vector<uint>(Prime3);
        var prime4 = new Vector<uint>(Prime4);
        while (index <= values.Length - Vector<uint>.Count)
        {
            var current = Vector.LoadUnsafe(ref valuesReference, (nuint)index);
            var hashes = hash + (current * prime3);
            hashes = RotateLeft(hashes, 17) * prime4;
            hashes ^= hashes >> 15;
            hashes *= prime2;
            hashes ^= hashes >> 13;
            hashes *= prime3;
            hashes ^= hashes >> 16;

            for (var lane = 0; lane < Vector<uint>.Count; lane++)
            {
                UpdateHash(new Hash32(hashes[lane]), remove);
            }

            index += Vector<uint>.Count;
        }

        foreach (var value in values[index..])
        {
            UpdateHash(Hash(value), remove);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateHash(Hash32 hash, bool remove)
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
    private static Hash32 Hash(ReadOnlySpan<byte> value)
    {
        return new(XxHash32.HashToUInt32(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<uint> RotateLeft(Vector<uint> value, int offset) => (value << offset) | (value >> (32 - offset));
}
