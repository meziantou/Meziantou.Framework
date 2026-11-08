using System.Buffers.Binary;
using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The XXH64 hash with a seed of 0 (the content checksum of a Zstandard frame, RFC 8878 section 3.1.1).</summary>
internal static class XxHash64
{
    private const ulong Prime1 = 0x9E3779B185EBCA87;
    private const ulong Prime2 = 0xC2B2AE3D27D4EB4F;
    private const ulong Prime3 = 0x165667B19E3779F9;
    private const ulong Prime4 = 0x85EBCA77C2B2AE63;
    private const ulong Prime5 = 0x27D4EB2F165667C5;

    public static ulong Compute(ReadOnlySpan<byte> data)
    {
        var length = (ulong)data.Length;
        ulong hash;
        if (data.Length >= 32)
        {
            var v1 = unchecked(Prime1 + Prime2);
            var v2 = Prime2;
            var v3 = 0UL;
            var v4 = unchecked(0 - Prime1);
            while (data.Length >= 32)
            {
                v1 = Round(v1, BinaryPrimitives.ReadUInt64LittleEndian(data));
                v2 = Round(v2, BinaryPrimitives.ReadUInt64LittleEndian(data[8..]));
                v3 = Round(v3, BinaryPrimitives.ReadUInt64LittleEndian(data[16..]));
                v4 = Round(v4, BinaryPrimitives.ReadUInt64LittleEndian(data[24..]));
                data = data[32..];
            }

            hash = BitOperations.RotateLeft(v1, 1) + BitOperations.RotateLeft(v2, 7) + BitOperations.RotateLeft(v3, 12) + BitOperations.RotateLeft(v4, 18);
            hash = MergeRound(hash, v1);
            hash = MergeRound(hash, v2);
            hash = MergeRound(hash, v3);
            hash = MergeRound(hash, v4);
        }
        else
        {
            hash = Prime5;
        }

        hash += length;
        while (data.Length >= 8)
        {
            hash ^= Round(0, BinaryPrimitives.ReadUInt64LittleEndian(data));
            hash = (BitOperations.RotateLeft(hash, 27) * Prime1) + Prime4;
            data = data[8..];
        }

        if (data.Length >= 4)
        {
            hash ^= BinaryPrimitives.ReadUInt32LittleEndian(data) * Prime1;
            hash = (BitOperations.RotateLeft(hash, 23) * Prime2) + Prime3;
            data = data[4..];
        }

        foreach (var value in data)
        {
            hash ^= value * Prime5;
            hash = BitOperations.RotateLeft(hash, 11) * Prime1;
        }

        hash ^= hash >> 33;
        hash *= Prime2;
        hash ^= hash >> 29;
        hash *= Prime3;
        hash ^= hash >> 32;
        return hash;
    }

    private static ulong Round(ulong accumulator, ulong input) => BitOperations.RotateLeft(accumulator + (input * Prime2), 31) * Prime1;

    private static ulong MergeRound(ulong accumulator, ulong value) => ((accumulator ^ Round(0, value)) * Prime1) + Prime4;
}
