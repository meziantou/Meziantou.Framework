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

        // Most coefficients are quantized to zero: the ones whose magnitude plus the offset is below the quantizer
        var threshold = quantizer - offset;
        for (var i = 0; i < coefficients.Length; i++)
        {
            var coefficient = coefficients[i];
            var magnitude = (long)Math.Abs(coefficient) * denominator;
            if (magnitude < threshold)
            {
                levels[i] = 0;
                continue;
            }

            var level = Math.Min((magnitude + offset) / quantizer, maxLevel);
            levels[i] = coefficient < 0 ? -(int)level : (int)level;
        }
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
