using System.IO.Hashing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.Arm;

namespace Meziantou.Framework.BloomFilters;

public sealed partial class BloomFilterXXHash64 : BloomFilter
{
    private const ulong Prime1 = 0x9E3779B185EBCA87UL;
    private const ulong Prime2 = 0xC2B2AE3D27D4EB4FUL;
    private const ulong Prime3 = 0x165667B19E3779F9UL;
    private const ulong Prime4 = 0x85EBCA77C2B2AE63UL;
    private const ulong Prime5 = 0x27D4EB2F165667C5UL;

    internal static void AddRangeCore<TSink>(TSink sink, ReadOnlySpan<long> values)
        where TSink : struct, IBloomFilterHashSink
    {
        AddRangeCore(sink, unsafe(MemoryMarshal.Cast<long, ulong>(values)));
    }

    internal static void AddRangeCore<TSink>(TSink sink, ReadOnlySpan<ulong> values)
        where TSink : struct, IBloomFilterHashSink
    {
        if (!BitConverter.IsLittleEndian)
        {
            foreach (var value in values)
            {
                sink.AddHash(Hash(value));
            }

            return;
        }

        var index = 0;

        // AdvSimd has no 64-bit lane multiply, so the JIT emulates the Vector<ulong> multiplications. The scalar
        // hash, which uses the native 64-bit multiply, is faster on Arm64.
        if (Vector.IsHardwareAccelerated && !AdvSimd.IsSupported && values.Length >= Vector<ulong>.Count)
        {
            var prime1 = new Vector<ulong>(Prime1);
            var prime2 = new Vector<ulong>(Prime2);
            var prime3 = new Vector<ulong>(Prime3);
            var prime4 = new Vector<ulong>(Prime4);
            var hash = new Vector<ulong>(Prime5 + sizeof(ulong));
            while (index <= values.Length - Vector<ulong>.Count)
            {
                var current = Vector.Create(values[index..]);
                var round = RotateLeft(current * prime2, 31) * prime1;
                var hashes = RotateLeft(hash ^ round, 27) * prime1 + prime4;
                hashes ^= hashes >> 33;
                hashes *= prime2;
                hashes ^= hashes >> 29;
                hashes *= prime3;
                hashes ^= hashes >> 32;

                for (var lane = 0; lane < Vector<ulong>.Count; lane++)
                {
                    sink.AddHash(BloomFilterHash.FromUInt64(hashes[lane]));
                }

                index += Vector<ulong>.Count;
            }
        }

        foreach (var value in values[index..])
        {
            sink.AddHash(BloomFilterHash.FromUInt64(HashUInt64(value)));
        }
    }

    /// <summary>Computes <see cref="XxHash64"/> of the little-endian bytes of <paramref name="value"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong HashUInt64(ulong value)
    {
        var round = BitOperations.RotateLeft(value * Prime2, 31) * Prime1;
        var hash = (BitOperations.RotateLeft((Prime5 + sizeof(ulong)) ^ round, 27) * Prime1) + Prime4;
        hash ^= hash >> 33;
        hash *= Prime2;
        hash ^= hash >> 29;
        hash *= Prime3;
        hash ^= hash >> 32;
        return hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static BloomFilterHash Hash(ReadOnlySpan<byte> value)
    {
        return BloomFilterHash.FromUInt64(XxHash64.HashToUInt64(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector<ulong> RotateLeft(Vector<ulong> value, int offset) => (value << offset) | (value >> (64 - offset));
}
