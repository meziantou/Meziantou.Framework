using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The quantization of the coefficients of a transform block: the inverse of the dequantization of the reconstruction
/// process (AV1 specification, section 7.12.3), which multiplies a level by the quantizer of its position and divides the
/// product by a denominator that depends on the transform size.
/// </summary>
internal static class Av1Quantizer
{
    /// <summary>The scale of a rounding offset: an offset of 128 rounds to nearest.</summary>
    public const int RoundingScale = 256;

    /// <summary>
    /// Quantizes <paramref name="coefficients"/>, in the scale of the dequantized coefficients, to
    /// <paramref name="levels"/>. A level is rounded down when the fractional part of the quotient is below
    /// 1 - <paramref name="rounding"/> / 256: a dead zone that trades a little distortion for fewer and smaller levels.
    /// </summary>
    /// <param name="coefficients">The coefficients, the DC coefficient first.</param>
    /// <param name="levels">The levels: as many as coefficients.</param>
    /// <param name="dcQuantizer"><c>get_dc_quant( plane )</c>.</param>
    /// <param name="acQuantizer"><c>get_ac_quant( plane )</c>.</param>
    /// <param name="denominator"><c>dqDenom</c> of the transform size.</param>
    /// <param name="bitDepth">The bit depth of the samples.</param>
    /// <param name="rounding">The rounding offset, in units of 1/256 of a quantizer step.</param>
    public static void Quantize(ReadOnlySpan<int> coefficients, Span<int> levels, int dcQuantizer, int acQuantizer, int denominator, int bitDepth, int rounding)
    {
        if (coefficients.IsEmpty)
            return;

        Quantize(coefficients[..1], levels, dcQuantizer, denominator, bitDepth, rounding);
        Quantize(coefficients[1..], levels[1..], acQuantizer, denominator, bitDepth, rounding);
    }

    private static void Quantize(ReadOnlySpan<int> coefficients, Span<int> levels, long quantizer, int denominator, int bitDepth, int rounding)
    {
        var offset = (quantizer * rounding) / RoundingScale;
        var maxLevel = GetMaxLevel(quantizer, denominator, bitDepth);

        // Most coefficients are quantized to zero: the ones whose magnitude plus the offset is below the quantizer. With
        // vectors, 4 coefficients whose magnitudes are all below the threshold are zeros (|c| * denominator < threshold is
        // |c| < ceil( threshold / denominator ), and the magnitude of int.MinValue as an unsigned value is above every
        // threshold); the coefficients of the other groups are quantized one at a time. The coefficients can be the levels
        var threshold = quantizer - offset;
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            var zeros = Vector128.Create(threshold > 0 ? (uint)((threshold + denominator - 1) / denominator) : 0u);
            for (; i <= coefficients.Length - Vector128<int>.Count; i += Vector128<int>.Count)
            {
                if (Vector128.LessThanAll(Vector128.Abs(Vector128.Create(coefficients[i..])).AsUInt32(), zeros))
                {
                    Vector128<int>.Zero.CopyTo(levels[i..]);
                    continue;
                }

                for (var k = i; k < i + Vector128<int>.Count; k++)
                {
                    levels[k] = Quantize(coefficients[k], quantizer, denominator, offset, threshold, maxLevel);
                }
            }
        }

        for (; i < coefficients.Length; i++)
        {
            levels[i] = Quantize(coefficients[i], quantizer, denominator, offset, threshold, maxLevel);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Quantize(int coefficient, long quantizer, int denominator, long offset, long threshold, long maxLevel)
    {
        var magnitude = (long)Math.Abs(coefficient) * denominator;
        if (magnitude < threshold)
            return 0;

        var level = Math.Min((magnitude + offset) / quantizer, maxLevel);
        return coefficient < 0 ? -(int)level : (int)level;
    }

    /// <summary>Returns the value that the reconstruction process dequantizes a level to.</summary>
    public static int Dequantize(int level, int quantizer, int denominator, int bitDepth)
    {
        var dq = (long)level * quantizer;
        var dq2 = (Math.Abs(dq) & 0xFFFFFF) / denominator;
        if (dq < 0)
            dq2 = -dq2;

        return (int)Math.Clamp(dq2, -(1 << (7 + bitDepth)), (1 << (7 + bitDepth)) - 1);
    }

    /// <summary>
    /// Returns the sum of the squared differences between <paramref name="coefficients"/> and the values that
    /// <see cref="Dequantize"/> gives for their <paramref name="levels"/>: the error of the quantization, in the scale of
    /// the coefficients.
    /// </summary>
    /// <param name="coefficients">The coefficients, the DC coefficient first.</param>
    /// <param name="levels">The levels of <see cref="Quantize(ReadOnlySpan{int}, Span{int}, int, int, int, int, int)"/>: as many as coefficients.</param>
    /// <param name="dcQuantizer"><c>get_dc_quant( plane )</c>.</param>
    /// <param name="acQuantizer"><c>get_ac_quant( plane )</c>.</param>
    /// <param name="denominator"><c>dqDenom</c> of the transform size: 1, 2 or 4.</param>
    /// <param name="bitDepth">The bit depth of the samples.</param>
    // The levels of Quantize are at most the largest level, whose product with the quantizer has 24 bits: the products of
    // the vectors are 32-bit. The division by the denominator, a power of two, of a magnitude is a shift, and a level of
    // zero dequantizes to zero. The squares and their sums are 64-bit integers, whose sum is exact in any order
    public static long GetQuantizationError(ReadOnlySpan<int> coefficients, ReadOnlySpan<int> levels, int dcQuantizer, int acQuantizer, int denominator, int bitDepth)
    {
        levels = levels[..coefficients.Length];
        if (!Av1Simd.IsSupported || coefficients.Length < Vector128<int>.Count)
            return GetQuantizationErrorScalar(coefficients, levels, dcQuantizer, acQuantizer, denominator, bitDepth);

        var shift = BitOperations.Log2((uint)denominator);
        var max = Vector128.Create((1 << (7 + bitDepth)) - 1);
        var min = Vector128.Create(-(1 << (7 + bitDepth)));
        var mask = Vector128.Create(0xFFFFFF);
        var quantizers = Vector128.Create(dcQuantizer, acQuantizer, acQuantizer, acQuantizer);
        var sum = Vector128<long>.Zero;
        var i = 0;
        for (; i <= coefficients.Length - Vector128<int>.Count; i += Vector128<int>.Count)
        {
            var difference = Vector128.Create(coefficients[i..]);
            var level = Vector128.Create(levels[i..]);
            if (level != Vector128<int>.Zero)
            {
                var product = level * quantizers;
                var magnitude = (Vector128.Abs(product) & mask) >> shift;
                var sign = product >> 31;
                difference -= Vector128.Min(Vector128.Max((magnitude ^ sign) - sign, min), max);
            }

            sum = Av1Simd.AddSquaresWidening(sum, difference);
            quantizers = Vector128.Create(acQuantizer);
        }

        return sum.GetElement(0) + sum.GetElement(1) + GetQuantizationErrorScalar(coefficients[i..], levels[i..], i is 0 ? dcQuantizer : acQuantizer, acQuantizer, denominator, bitDepth);
    }

    /// <summary>The scalar reference of <see cref="GetQuantizationError"/>.</summary>
    public static long GetQuantizationErrorScalar(ReadOnlySpan<int> coefficients, ReadOnlySpan<int> levels, int dcQuantizer, int acQuantizer, int denominator, int bitDepth)
    {
        long error = 0;
        for (var i = 0; i < coefficients.Length; i++)
        {
            long difference = coefficients[i] - Dequantize(levels[i], i is 0 ? dcQuantizer : acQuantizer, denominator, bitDepth);
            error += difference * difference;
        }

        return error;
    }

    // The largest level whose product with the quantizer fits in the 24 bits that the dequantization keeps, and whose
    // dequantized value is in the range of the inverse transform
    private static long GetMaxLevel(long quantizer, int denominator, int bitDepth)
    {
        var maxProduct = Math.Min(0xFFFFFF, (((1L << (7 + bitDepth)) - 1) * denominator) + denominator - 1);
        return maxProduct / quantizer;
    }
}
