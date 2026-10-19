using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Fonts;

/// <summary>The Adler-32 checksum of RFC 1950 (zlib): the checksum of the tables of a WOFF file, and of the zlib streams of the image codecs.</summary>
internal static class Adler32
{
    /// <summary>Gets the initial running value of <see cref="Update"/>.</summary>
    public const uint Initial = 1;

    /// <summary>Computes the Adler-32 checksum of RFC 1950.</summary>
    public static uint Compute(ReadOnlySpan<byte> data) => Update(Initial, data);

    /// <summary>Updates a running Adler-32 checksum (RFC 1950) with more bytes; start from <see cref="Initial"/>.</summary>
    /// <remarks>
    /// With 128-bit hardware vectors, whole 16-byte blocks are summed in vector lanes. For a run of <c>n</c> bytes
    /// <c>x0..x(n-1)</c>, RFC 1950 gives <c>A = A0 + sum x</c> and <c>B = B0 + n A0 + sum (n - j) x_j</c>. Split into 16-byte blocks
    /// <c>m = 0..k-1</c> of sums <c>S_m</c>, the weight of byte <c>i</c> of block <c>m</c> is <c>16 (k - 1 - m) + (16 - i)</c>, so
    /// <c>B = B0 + n A0 + 16 sum_m (S_0 + ... + S_(m-1)) + sum_m sum_i (16 - i) x_(m,i)</c>: the lanes accumulate the block
    /// sums, their running prefix and the in-block weighted sums. Runs are short enough for 32-bit lanes; the result equals
    /// the byte-at-a-time reference (<see cref="UpdateScalar"/>).
    /// </remarks>
    public static uint Update(uint adler, ReadOnlySpan<byte> data)
    {
        if (!Vector128.IsHardwareAccelerated || data.Length < 64)
            return UpdateScalar(adler, data);

        const uint Modulus = 65521;

        // 5536 = 346 blocks of 16 bytes (at most the RFC 1950 bound of 5552): the lane sums stay far below 2^32
        const int RunLength = 5536;
        var a = adler & 0xFFFF;
        var b = adler >> 16;
        var weightsLow = Vector128.Create((ushort)16, 15, 14, 13, 12, 11, 10, 9);
        var weightsHigh = Vector128.Create((ushort)8, 7, 6, 5, 4, 3, 2, 1);
        while (data.Length >= 16)
        {
            var run = data[..Math.Min(RunLength, data.Length & ~15)];
            ref var start = ref unsafe(MemoryMarshal.GetReference(run));
            var sums = Vector128<uint>.Zero;
            var prefixes = Vector128<uint>.Zero;
            var weighted = Vector128<uint>.Zero;
            for (var offset = 0; offset < run.Length; offset += 16)
            {
                prefixes += sums;
                var (low, high) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref start, (nuint)offset)));
                sums += WidenSum(low + high);
                weighted += WidenSum((low * weightsLow) + (high * weightsHigh));
            }

            var n = (ulong)run.Length;
            var total = (ulong)Vector128.Sum(sums);
            b = (uint)((b + (n * a) + (16 * (ulong)Vector128.Sum(prefixes)) + Vector128.Sum(weighted)) % Modulus);
            a = (uint)((a + total) % Modulus);
            data = data[run.Length..];
        }

        return UpdateScalar((b << 16) | a, data);

        static Vector128<uint> WidenSum(Vector128<ushort> values)
        {
            var (low, high) = Vector128.Widen(values);
            return low + high;
        }
    }

    /// <summary>The byte-at-a-time reference of <see cref="Update"/>.</summary>
    public static uint UpdateScalar(uint adler, ReadOnlySpan<byte> data)
    {
        const uint Modulus = 65521;

        // 5552 is the largest block for which the sums cannot overflow 32 bits (RFC 1950)
        const int BlockSize = 5552;
        var a = adler & 0xFFFF;
        var b = adler >> 16;
        while (!data.IsEmpty)
        {
            var block = data[..Math.Min(BlockSize, data.Length)];
            foreach (var value in block)
            {
                a += value;
                b += a;
            }

            a %= Modulus;
            b %= Modulus;
            data = data[block.Length..];
        }

        return (b << 16) | a;
    }
}
