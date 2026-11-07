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

    // The largest level whose product with the quantizer fits in the 24 bits that the dequantization keeps, and whose
    // dequantized value is in the range of the inverse transform
    private static long GetMaxLevel(long quantizer, int denominator, int bitDepth)
    {
        var maxProduct = Math.Min(0xFFFFFF, (((1L << (7 + bitDepth)) - 1) * denominator) + denominator - 1);
        return maxProduct / quantizer;
    }
}
