using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.BloomFilters;

/// <summary>
/// The pair of hashes used for double hashing: the i-th position is <c>Reduce(Hash1 + i * Hash2, range)</c>.
/// </summary>
/// <remarks>
/// Both halves are 64 bits wide whatever the width of the source hash. The multiply-shift reduction can only produce as
/// many distinct positions as the accumulator has values, so 32-bit halves cap a filter at 2^32 reachable positions: a
/// 2^34-bit filter would only ever use every fourth bit, silently multiplying its false positive rate.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly struct BloomFilterHash
{
    private const ulong GoldenRatio = 0x9E3779B97F4A7C15UL;

    public readonly ulong Hash1;
    public readonly ulong Hash2;

    private BloomFilterHash(ulong hash1, ulong hash2)
    {
        Hash1 = hash1;
        Hash2 = hash2;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BloomFilterHash FromUInt32(uint value)
    {
        // A 32-bit hash has too little entropy to be split into two halves, so it is spread over 64 bits first.
        return FromUInt64(SplitMix64(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BloomFilterHash FromUInt64(ulong value)
    {
        // Splitting a 64-bit hash into two halves would leave 32-bit halves, so the step is derived by remixing the
        // whole value instead. An odd step keeps the positions from cycling before the word size is exhausted.
        return new BloomFilterHash(value, SplitMix64(value) | 1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BloomFilterHash FromUInt128(UInt128 value)
    {
        var high = (ulong)(value >> 64);
        return new BloomFilterHash((ulong)value, high == 0 ? GoldenRatio : high);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static BloomFilterHash FromUInt128(ulong low, ulong high)
    {
        return new BloomFilterHash(low, high == 0 ? GoldenRatio : high);
    }

    /// <summary>
    /// Maps a 64-bit hash to <c>[0, range)</c> without a division.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Reduce(ulong hash, ulong range) => (long)(((UInt128)hash * range) >> 64);

    /// <summary>
    /// The splitmix64 finalizer, which spreads every input bit over the whole output.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong SplitMix64(ulong value)
    {
        var result = value + GoldenRatio;
        result = (result ^ (result >> 30)) * 0xBF58476D1CE4E5B9UL;
        result = (result ^ (result >> 27)) * 0x94D049BB133111EBUL;
        return result ^ (result >> 31);
    }
}
