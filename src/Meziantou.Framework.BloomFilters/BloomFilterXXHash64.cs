using System.IO.Hashing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.BloomFilters;

public sealed partial class BloomFilterXXHash64 : BloomFilter
{
    private const ulong Prime1 = 0x9E3779B185EBCA87UL;
    private const ulong Prime2 = 0xC2B2AE3D27D4EB4FUL;
    private const ulong Prime3 = 0x165667B19E3779F9UL;
    private const ulong Prime4 = 0x85EBCA77C2B2AE63UL;
    private const ulong Prime5 = 0x27D4EB2F165667C5UL;

    private void AddRangeCore(ReadOnlySpan<long> values)
    {
        AddRangeCore(MemoryMarshal.Cast<long, ulong>(values));
    }

    private void AddRangeCore(ReadOnlySpan<ulong> values)
    {
        if (!BitConverter.IsLittleEndian || !Vector.IsHardwareAccelerated || values.Length < Vector<ulong>.Count)
        {
            foreach (var value in values)
            {
                Add(value);
            }

            return;
        }

        ref var valuesReference = ref MemoryMarshal.GetReference(values);
        var index = 0;
        var prime1 = new Vector<ulong>(Prime1);
        var prime2 = new Vector<ulong>(Prime2);
        var prime3 = new Vector<ulong>(Prime3);
        var prime4 = new Vector<ulong>(Prime4);
        var hash = new Vector<ulong>(Prime5 + sizeof(ulong));
        while (index <= values.Length - Vector<ulong>.Count)
        {
            var current = Vector.LoadUnsafe(ref valuesReference, (nuint)index);
            var round = RotateLeft(current * prime2, 31) * prime1;
            var hashes = RotateLeft(hash ^ round, 27) * prime1 + prime4;
            hashes ^= hashes >> 33;
            hashes *= prime2;
            hashes ^= hashes >> 29;
            hashes *= prime3;
            hashes ^= hashes >> 32;

            for (var lane = 0; lane < Vector<ulong>.Count; lane++)
            {
                AddHash(new Hash64(hashes[lane]));
            }

            index += Vector<ulong>.Count;
        }

        foreach (var value in values[index..])
        {
            Add(value);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Hash64 Hash(ReadOnlySpan<byte> value)
    {
        return new(XxHash64.HashToUInt64(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<ulong> RotateLeft(Vector<ulong> value, int offset) => (value << offset) | (value >> (64 - offset));
}
