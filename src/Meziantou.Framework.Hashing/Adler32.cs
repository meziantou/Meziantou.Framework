using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Hashing;

/// <summary>Computes the Adler-32 checksum of RFC 1950: the checksum of zlib streams.</summary>
/// <remarks>
/// <para>The implementation uses the vector instructions of the processor (AdvSimd, AVX2, SSSE3) when they are available.</para>
/// <para>
/// The vector paths sum whole blocks of <c>w</c> bytes (16 or 32) in vector lanes. For a run of <c>n</c> bytes <c>x0..x(n-1)</c>,
/// RFC 1950 gives <c>A = A0 + sum x</c> and <c>B = B0 + n A0 + sum (n - j) x_j</c>. Split into blocks <c>m = 0..k-1</c> of sums
/// <c>S_m</c>, the weight of byte <c>i</c> of block <c>m</c> is <c>w (k - 1 - m) + (w - i)</c>, so
/// <c>B = B0 + n A0 + w sum_m (S_0 + ... + S_(m-1)) + sum_m sum_i (w - i) x_(m,i)</c>: the lanes accumulate the block sums, their
/// running prefix and the in-block weighted sums. Runs are short enough for 32-bit lanes (and for the 16-bit column sums of
/// <see cref="UpdateAdvSimd"/>); every path equals the byte-at-a-time reference (<see cref="UpdateScalar"/>).
/// </para>
/// </remarks>
public static class Adler32
{
    // The Adler-32 of no data
    private const uint Initial = 1;

    private const uint Modulus = 65521;

    // 5504 = 172 blocks of 32 bytes (at most the RFC 1950 bound of 5552): the 32-bit lane sums stay far below 2^32, and
    // the 16-bit column sums of the AdvSimd path below 172 * 255 = 43,860
    private const int RunLength = 5504;

    /// <summary>Computes the Adler-32 checksum of data.</summary>
    /// <param name="data">The data.</param>
    /// <returns>The Adler-32 checksum of <paramref name="data"/>; 1 when it is empty.</returns>
    public static uint Compute(ReadOnlySpan<byte> data) => Update(Initial, data);

    /// <summary>Computes the Adler-32 checksum of data that follows other data, from the checksum of the other data.</summary>
    /// <param name="checksum">The Adler-32 checksum of the preceding data: 1 when there is none.</param>
    /// <param name="data">The data that follows.</param>
    /// <returns>The Adler-32 checksum of the preceding data followed by <paramref name="data"/>.</returns>
    /// <remarks><c>Append(Compute(a), b)</c> is <c>Compute(a + b)</c>: use it to compute the checksum of data received in several parts.</remarks>
    public static uint Append(uint checksum, ReadOnlySpan<byte> data) => Update(checksum, data);

    /// <summary>Updates a running Adler-32 checksum (RFC 1950) with more bytes; start from 1.</summary>
    internal static uint Update(uint adler, ReadOnlySpan<byte> data)
    {
        if (data.Length < 64)
            return UpdateScalar(adler, data);

        if (AdvSimd.IsSupported)
            return UpdateAdvSimd(adler, data);

        if (Avx2.IsSupported)
            return UpdateAvx2(adler, data);

        if (Ssse3.IsSupported)
            return UpdateSsse3(adler, data);

        if (Vector128.IsHardwareAccelerated)
            return UpdateVector128(adler, data);

        return UpdateScalar(adler, data);
    }

    /// <summary>
    /// The Arm path: 32 bytes per step. Pairwise widening adds give the block sums, and each byte position keeps a 16-bit
    /// column sum whose weight is applied once per run with widening multiply-adds.
    /// </summary>
    internal static uint UpdateAdvSimd(uint adler, ReadOnlySpan<byte> data)
    {
        var a = adler & 0xFFFF;
        var b = adler >> 16;
        var weights0 = Vector128.Create((ushort)32, 31, 30, 29, 28, 27, 26, 25);
        var weights1 = Vector128.Create((ushort)24, 23, 22, 21, 20, 19, 18, 17);
        var weights2 = Vector128.Create((ushort)16, 15, 14, 13, 12, 11, 10, 9);
        var weights3 = Vector128.Create((ushort)8, 7, 6, 5, 4, 3, 2, 1);
        while (data.Length >= 32)
        {
            var run = data[..Math.Min(RunLength, data.Length & ~31)];
            ref var start = ref unsafe(MemoryMarshal.GetReference(run));
            var sums = Vector128<uint>.Zero;
            var prefixes = Vector128<uint>.Zero;
            var columns0 = Vector128<ushort>.Zero;
            var columns1 = Vector128<ushort>.Zero;
            var columns2 = Vector128<ushort>.Zero;
            var columns3 = Vector128<ushort>.Zero;
            for (nuint offset = 0; offset < (nuint)run.Length; offset += 32)
            {
                var low = unsafe(Vector128.LoadUnsafe(ref start, offset));
                var high = unsafe(Vector128.LoadUnsafe(ref start, offset + 16));
                prefixes += sums;
                sums = AdvSimd.AddPairwiseWideningAndAdd(sums, AdvSimd.AddPairwiseWideningAndAdd(AdvSimd.AddPairwiseWidening(low), high));
                columns0 = AdvSimd.AddWideningLower(columns0, low.GetLower());
                columns1 = AdvSimd.AddWideningUpper(columns1, low);
                columns2 = AdvSimd.AddWideningLower(columns2, high.GetLower());
                columns3 = AdvSimd.AddWideningUpper(columns3, high);
            }

            var weighted = AdvSimd.MultiplyWideningLower(columns0.GetLower(), weights0.GetLower());
            weighted = AdvSimd.MultiplyWideningUpperAndAdd(weighted, columns0, weights0);
            weighted = AdvSimd.MultiplyWideningLowerAndAdd(weighted, columns1.GetLower(), weights1.GetLower());
            weighted = AdvSimd.MultiplyWideningUpperAndAdd(weighted, columns1, weights1);
            weighted = AdvSimd.MultiplyWideningLowerAndAdd(weighted, columns2.GetLower(), weights2.GetLower());
            weighted = AdvSimd.MultiplyWideningUpperAndAdd(weighted, columns2, weights2);
            weighted = AdvSimd.MultiplyWideningLowerAndAdd(weighted, columns3.GetLower(), weights3.GetLower());
            weighted = AdvSimd.MultiplyWideningUpperAndAdd(weighted, columns3, weights3);
            AddRun(ref a, ref b, run.Length, 32, Vector128.Sum(sums), Vector128.Sum(prefixes), Vector128.Sum(weighted));
            data = data[run.Length..];
        }

        return UpdateScalar((b << 16) | a, data);
    }

    /// <summary>The AVX2 path: 32 bytes per step, summed by <c>vpsadbw</c> and weighted by <c>vpmaddubsw</c> + <c>vpmaddwd</c>.</summary>
    internal static uint UpdateAvx2(uint adler, ReadOnlySpan<byte> data)
    {
        var a = adler & 0xFFFF;
        var b = adler >> 16;
        var weights = Vector256.Create((sbyte)32, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17, 16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1);
        var ones = Vector256.Create((short)1);
        while (data.Length >= 32)
        {
            var run = data[..Math.Min(RunLength, data.Length & ~31)];
            ref var start = ref unsafe(MemoryMarshal.GetReference(run));
            var sums = Vector256<uint>.Zero;
            var prefixes = Vector256<uint>.Zero;
            var weighted = Vector256<uint>.Zero;
            for (nuint offset = 0; offset < (nuint)run.Length; offset += 32)
            {
                var values = unsafe(Vector256.LoadUnsafe(ref start, offset));
                prefixes += sums;
                sums += Avx2.SumAbsoluteDifferences(values, Vector256<byte>.Zero).AsUInt32();
                weighted += Avx2.MultiplyAddAdjacent(Avx2.MultiplyAddAdjacent(values, weights), ones).AsUInt32();
            }

            AddRun(ref a, ref b, run.Length, 32, Vector256.Sum(sums), Vector256.Sum(prefixes), Vector256.Sum(weighted));
            data = data[run.Length..];
        }

        return UpdateScalar((b << 16) | a, data);
    }

    /// <summary>The SSSE3 path: 32 bytes per step, summed by <c>psadbw</c> and weighted by <c>pmaddubsw</c> + <c>pmaddwd</c>.</summary>
    internal static uint UpdateSsse3(uint adler, ReadOnlySpan<byte> data)
    {
        var a = adler & 0xFFFF;
        var b = adler >> 16;
        var weightsLow = Vector128.Create((sbyte)32, 31, 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 19, 18, 17);
        var weightsHigh = Vector128.Create((sbyte)16, 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1);
        var ones = Vector128.Create((short)1);
        while (data.Length >= 32)
        {
            var run = data[..Math.Min(RunLength, data.Length & ~31)];
            ref var start = ref unsafe(MemoryMarshal.GetReference(run));
            var sums = Vector128<uint>.Zero;
            var prefixes = Vector128<uint>.Zero;
            var weighted = Vector128<uint>.Zero;
            for (nuint offset = 0; offset < (nuint)run.Length; offset += 32)
            {
                var low = unsafe(Vector128.LoadUnsafe(ref start, offset));
                var high = unsafe(Vector128.LoadUnsafe(ref start, offset + 16));
                prefixes += sums;
                sums += (Sse2.SumAbsoluteDifferences(low, Vector128<byte>.Zero) + Sse2.SumAbsoluteDifferences(high, Vector128<byte>.Zero)).AsUInt32();

                // A pair of bytes weighs at most 255 * (32 + 31) and the two halves 255 * (32 + 31 + 16 + 15) < 2^15
                var products = Ssse3.MultiplyAddAdjacent(low, weightsLow) + Ssse3.MultiplyAddAdjacent(high, weightsHigh);
                weighted += Sse2.MultiplyAddAdjacent(products, ones).AsUInt32();
            }

            AddRun(ref a, ref b, run.Length, 32, Vector128.Sum(sums), Vector128.Sum(prefixes), Vector128.Sum(weighted));
            data = data[run.Length..];
        }

        return UpdateScalar((b << 16) | a, data);
    }

    /// <summary>The portable path of the platforms without a dedicated path: 16 bytes per step, widened to 32-bit lanes.</summary>
    internal static uint UpdateVector128(uint adler, ReadOnlySpan<byte> data)
    {
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
            for (nuint offset = 0; offset < (nuint)run.Length; offset += 16)
            {
                prefixes += sums;
                var (low, high) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref start, offset)));
                sums += WidenSum(low + high);
                weighted += WidenSum((low * weightsLow) + (high * weightsHigh));
            }

            AddRun(ref a, ref b, run.Length, 16, Vector128.Sum(sums), Vector128.Sum(prefixes), Vector128.Sum(weighted));
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
    internal static uint UpdateScalar(uint adler, ReadOnlySpan<byte> data)
    {
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

    /// <summary>Adds the lane totals of a run of whole blocks of <paramref name="blockLength"/> bytes to the running sums.</summary>
    private static void AddRun(ref uint a, ref uint b, int length, int blockLength, uint sum, uint prefixSum, uint weightedSum)
    {
        b = (uint)((b + ((ulong)length * a) + ((ulong)blockLength * prefixSum) + weightedSum) % Modulus);
        a = (uint)((a + (ulong)sum) % Modulus);
    }
}
