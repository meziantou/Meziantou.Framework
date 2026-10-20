using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The color transforms that end the decoding of a frame and do not depend on the image (ISO/IEC 18181-1, annex L): from
/// YCbCr to RGB samples where 1 is the largest value of an integer sample, and the sRGB transfer function.
/// </summary>
internal static class JpegXlColorTransform
{
    private const float YCbCrOffset = 128f / 255;
    private const float RedFromCr = 1.402f;
    private const float GreenFromCb = -0.114f * 1.772f / 0.587f;
    private const float GreenFromCr = -0.299f * 1.402f / 0.587f;
    private const float BlueFromCb = 1.772f;

    // The sRGB transfer function
    private const float SrgbLinearLimit = 0.0031308f;
    private const float SrgbLinearSlope = 12.92f;
    private const float SrgbScale = 1.055f;
    private const float SrgbOffset = 0.055f;

    // x^(5/12) (PowerFiveTwelfths): the fields of a float, the quotient by 12 of 5 e + 768 (exact up to 130,000), and the
    // minimax polynomials of m^(5/12) for m - 1 in [0, 1) and of 2^(r/12) for r in 0..11, fitted with the Remez algorithm
    // on the relative error and rounded to float
    private const int MantissaMask = 0x7FFFFF;
    private const int OneBits = 0x3F800000;
    private const int ExponentOffset = 768 - (5 * 127);
    private const int DivideBy12Multiplier = 43691;
    private const int DivideBy12Shift = 19;
    private const float M0 = 1f;
    private const float M1 = 0.41666332f;
    private const float M2 = -0.12144905f;
    private const float M3 = 0.06342468f;
    private const float M4 = -0.038142275f;
    private const float M5 = 0.02099892f;
    private const float M6 = -0.008220621f;
    private const float M7 = 0.0015648875f;
    private const float E0 = 0.99999994f;
    private const float E1 = 0.05776258f;
    private const float E2 = 0.0016678864f;
    private const float E3 = 3.2261967E-05f;
    private const float E4 = 4.387566E-07f;
    private const float E5 = 7.3357787E-09f;

    /// <summary>
    /// Converts rows of YCbCr samples as JPEG defines them (full range, chroma centered on zero, luma centered on one half)
    /// to RGB samples, in place.
    /// </summary>
    /// <param name="cb">The Cb samples, replaced with the red ones.</param>
    /// <param name="y">The Y samples, replaced with the green ones.</param>
    /// <param name="cr">The Cr samples, replaced with the blue ones.</param>
    public static void YCbCrToRgb(Span<float> cb, Span<float> y, Span<float> cr)
    {
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            // The operations of the scalar loop, four samples at a time
            var offset = Vector128.Create(YCbCrOffset);
            var redFromCr = Vector128.Create(RedFromCr);
            var greenFromCb = Vector128.Create(GreenFromCb);
            var greenFromCr = Vector128.Create(GreenFromCr);
            var blueFromCb = Vector128.Create(BlueFromCb);
            for (; i <= y.Length - 4; i += 4)
            {
                var luma = Vector128.Create(y.Slice(i, 4)) + offset;
                var blueDifference = Vector128.Create(cb.Slice(i, 4));
                var redDifference = Vector128.Create(cr.Slice(i, 4));
                (luma + (redFromCr * redDifference)).CopyTo(cb.Slice(i, 4));
                (luma + (greenFromCb * blueDifference) + (greenFromCr * redDifference)).CopyTo(y.Slice(i, 4));
                (luma + (blueFromCb * blueDifference)).CopyTo(cr.Slice(i, 4));
            }
        }

        YCbCrToRgbScalar(cb[i..], y[i..], cr[i..]);
    }

    /// <summary>The scalar reference of <see cref="YCbCrToRgb"/>.</summary>
    internal static void YCbCrToRgbScalar(Span<float> cb, Span<float> y, Span<float> cr)
    {
        for (var i = 0; i < y.Length; i++)
        {
            var luma = y[i] + YCbCrOffset;
            var blueDifference = cb[i];
            var redDifference = cr[i];
            cb[i] = luma + (RedFromCr * redDifference);
            y[i] = luma + (GreenFromCb * blueDifference) + (GreenFromCr * redDifference);
            cr[i] = luma + (BlueFromCb * blueDifference);
        }
    }

    /// <summary>
    /// Applies the sRGB transfer function to linear samples, in place; a negative sample is the opposite of the encoding of
    /// its opposite. Each lane performs the operations of <see cref="LinearToSrgbScalar"/> in the same order.
    /// </summary>
    public static void LinearToSrgb(Span<float> samples)
    {
        ref var start = ref unsafe(MemoryMarshal.GetReference(samples));
        var i = 0;
        if (Vector256.IsHardwareAccelerated)
        {
            for (; i <= samples.Length - 8; i += 8)
            {
                unsafe
                {
                    EncodeSrgb(Vector256.LoadUnsafe(ref start, (nuint)i)).StoreUnsafe(ref start, (nuint)i);
                }
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            for (; i <= samples.Length - 4; i += 4)
            {
                unsafe
                {
                    EncodeSrgb(Vector128.LoadUnsafe(ref start, (nuint)i)).StoreUnsafe(ref start, (nuint)i);
                }
            }
        }

        LinearToSrgbScalar(samples[i..]);
    }

    /// <summary>The scalar reference of <see cref="LinearToSrgb"/>.</summary>
    internal static void LinearToSrgbScalar(Span<float> samples)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = EncodeSrgb(samples[i]);
        }
    }

    /// <summary>Encodes one linear sample with the sRGB transfer function.</summary>
    private static float EncodeSrgb(float value)
    {
        var magnitude = MathF.Abs(value);
        var power = float.IsFinite(magnitude) ? PowerFiveTwelfths(magnitude) : magnitude;

        var encoded = magnitude <= SrgbLinearLimit ? SrgbLinearSlope * magnitude : (SrgbScale * power) - SrgbOffset;
        return MathF.CopySign(encoded, value);
    }

    private static Vector128<float> EncodeSrgb(Vector128<float> value)
    {
        var magnitude = Vector128.Abs(value);
        var power = Vector128.ConditionalSelect(Vector128.LessThan(magnitude, Vector128.Create(float.PositiveInfinity)), PowerFiveTwelfths(magnitude), magnitude);
        var encoded = Vector128.ConditionalSelect(Vector128.LessThanOrEqual(magnitude, Vector128.Create(SrgbLinearLimit)), Vector128.Create(SrgbLinearSlope) * magnitude, (Vector128.Create(SrgbScale) * power) - Vector128.Create(SrgbOffset));
        return Vector128.CopySign(encoded, value);
    }

    private static Vector256<float> EncodeSrgb(Vector256<float> value)
    {
        var magnitude = Vector256.Abs(value);
        var power = Vector256.ConditionalSelect(Vector256.LessThan(magnitude, Vector256.Create(float.PositiveInfinity)), PowerFiveTwelfths(magnitude), magnitude);
        var encoded = Vector256.ConditionalSelect(Vector256.LessThanOrEqual(magnitude, Vector256.Create(SrgbLinearLimit)), Vector256.Create(SrgbLinearSlope) * magnitude, (Vector256.Create(SrgbScale) * power) - Vector256.Create(SrgbOffset));
        return Vector256.CopySign(encoded, value);
    }

    /// <summary>
    /// Approximates <c>x^(1/2.4)</c> for a finite positive <paramref name="magnitude"/>, without a call to a math library so
    /// that the vector paths give the same results: with <c>x = 2^e m</c>, where <c>m</c> is in [1, 2), and
    /// <c>5 e / 12 = n + r / 12</c>, the result is <c>2^n 2^(r/12) m^(5/12)</c>, where both factors are minimax polynomials
    /// (of <c>r</c> and of <c>m - 1</c>). The relative error is below 3e-7 (<c>MathF.Pow</c>: 1.1e-7).
    /// </summary>
    internal static float PowerFiveTwelfths(float magnitude)
    {
        var bits = BitConverter.SingleToInt32Bits(magnitude);
        var mantissa = BitConverter.Int32BitsToSingle((bits & MantissaMask) | OneBits);

        // 5 e + 768, with e the unbiased exponent, is positive: its quotient by 12 is n + 64, computed with a multiplication
        var shifted = (5 * (bits >>> 23)) + ExponentOffset;
        var quotient = (shifted * DivideBy12Multiplier) >> DivideBy12Shift;
        var remainder = (float)(shifted - (12 * quotient));
        var scale = BitConverter.Int32BitsToSingle((quotient + 63) << 23);
        var t = mantissa - 1;
        var root = (M7 * t) + M6;
        root = (root * t) + M5;
        root = (root * t) + M4;
        root = (root * t) + M3;
        root = (root * t) + M2;
        root = (root * t) + M1;
        root = (root * t) + M0;
        var fraction = (E5 * remainder) + E4;
        fraction = (fraction * remainder) + E3;
        fraction = (fraction * remainder) + E2;
        fraction = (fraction * remainder) + E1;
        fraction = (fraction * remainder) + E0;
        return scale * (fraction * root);
    }

    private static Vector128<float> PowerFiveTwelfths(Vector128<float> magnitude)
    {
        var bits = magnitude.AsInt32();
        var mantissa = ((bits & Vector128.Create(MantissaMask)) | Vector128.Create(OneBits)).AsSingle();
        var shifted = (Vector128.Create(5) * (bits >>> 23)) + Vector128.Create(ExponentOffset);
        var quotient = (shifted * Vector128.Create(DivideBy12Multiplier)) >> DivideBy12Shift;
        var remainder = Vector128.ConvertToSingle(shifted - (Vector128.Create(12) * quotient));
        var scale = ((quotient + Vector128.Create(63)) << 23).AsSingle();
        var t = mantissa - Vector128<float>.One;
        var root = (Vector128.Create(M7) * t) + Vector128.Create(M6);
        root = (root * t) + Vector128.Create(M5);
        root = (root * t) + Vector128.Create(M4);
        root = (root * t) + Vector128.Create(M3);
        root = (root * t) + Vector128.Create(M2);
        root = (root * t) + Vector128.Create(M1);
        root = (root * t) + Vector128.Create(M0);
        var fraction = (Vector128.Create(E5) * remainder) + Vector128.Create(E4);
        fraction = (fraction * remainder) + Vector128.Create(E3);
        fraction = (fraction * remainder) + Vector128.Create(E2);
        fraction = (fraction * remainder) + Vector128.Create(E1);
        fraction = (fraction * remainder) + Vector128.Create(E0);
        return scale * (fraction * root);
    }

    private static Vector256<float> PowerFiveTwelfths(Vector256<float> magnitude)
    {
        var bits = magnitude.AsInt32();
        var mantissa = ((bits & Vector256.Create(MantissaMask)) | Vector256.Create(OneBits)).AsSingle();
        var shifted = (Vector256.Create(5) * (bits >>> 23)) + Vector256.Create(ExponentOffset);
        var quotient = (shifted * Vector256.Create(DivideBy12Multiplier)) >> DivideBy12Shift;
        var remainder = Vector256.ConvertToSingle(shifted - (Vector256.Create(12) * quotient));
        var scale = ((quotient + Vector256.Create(63)) << 23).AsSingle();
        var t = mantissa - Vector256<float>.One;
        var root = (Vector256.Create(M7) * t) + Vector256.Create(M6);
        root = (root * t) + Vector256.Create(M5);
        root = (root * t) + Vector256.Create(M4);
        root = (root * t) + Vector256.Create(M3);
        root = (root * t) + Vector256.Create(M2);
        root = (root * t) + Vector256.Create(M1);
        root = (root * t) + Vector256.Create(M0);
        var fraction = (Vector256.Create(E5) * remainder) + Vector256.Create(E4);
        fraction = (fraction * remainder) + Vector256.Create(E3);
        fraction = (fraction * remainder) + Vector256.Create(E2);
        fraction = (fraction * remainder) + Vector256.Create(E1);
        fraction = (fraction * remainder) + Vector256.Create(E0);
        return scale * (fraction * root);
    }
}
