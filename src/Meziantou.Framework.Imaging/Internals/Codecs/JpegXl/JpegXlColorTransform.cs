using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

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

    // The vector paths read 2^n 2^(r/12) from a table for the 16 exponents of 2^-9 to 2^6 (the samples above the linear
    // segment, up to 128): its product with m^(5/12) is the one of PowerFiveTwelfths, since both factors are exact powers of
    // two times the same float and the result is a normal float
    private const int FirstTableExponent = 118;
    private const int LastTableBits = ((FirstTableExponent + 16) << 23) - 1;
    private static readonly float[] ExponentFactors = CreateExponentFactors();

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
        var length = samples.Length;
        var i = 0;
        if (Vector256.IsHardwareAccelerated && Avx2.IsSupported)
        {
            // Runs of vectors whose magnitudes are below 128 (or not a number), then one vector with the general power
            while (true)
            {
                i = EncodeSrgbRunVector256(ref start, i, length);
                if (i > length - 8)
                    break;

                unsafe
                {
                    EncodeSrgb(Vector256.LoadUnsafe(ref start, (nuint)i)).StoreUnsafe(ref start, (nuint)i);
                }

                i += 8;
            }
        }
        else if (AdvSimd.Arm64.IsSupported)
        {
            while (true)
            {
                i = EncodeSrgbRunArm64(ref start, i, length);
                if (i > length - 4)
                    break;

                unsafe
                {
                    EncodeSrgb(Vector128.LoadUnsafe(ref start, (nuint)i)).StoreUnsafe(ref start, (nuint)i);
                }

                i += 4;
            }
        }

        if (Vector128.IsHardwareAccelerated)
        {
            for (; i <= length - 4; i += 4)
            {
                unsafe
                {
                    EncodeSrgb(Vector128.LoadUnsafe(ref start, (nuint)i)).StoreUnsafe(ref start, (nuint)i);
                }
            }
        }

        LinearToSrgbScalar(samples[i..]);
    }

    /// <summary>
    /// Encodes the vectors of eight samples from <paramref name="i"/> on, up to the first one with a magnitude of 128 or
    /// more (or not a number), whose position is returned: their power is the product of the factor of their exponent, from
    /// <see cref="ExponentFactors"/>, and of the power of their mantissa. The loop calls nothing, so its constants stay in
    /// registers.
    /// </summary>
    private static int EncodeSrgbRunVector256(ref float start, int i, int length)
    {
        var factors = ExponentFactors;
        var lowFactors = Vector256.Create(factors.AsSpan(0, 8));
        var highFactors = Vector256.Create(factors.AsSpan(8, 8));
        var lastTableBits = Vector256.Create(LastTableBits);
        var firstExponent = Vector256.Create(FirstTableExponent);
        for (; i <= length - 8; i += 8)
        {
            unsafe
            {
                var value = Vector256.LoadUnsafe(ref start, (nuint)i);
                var magnitude = Vector256.Abs(value);
                if (Vector256.GreaterThanAny(magnitude.AsInt32(), lastTableBits))
                    return i;

                // Below the table, the sample is in the linear segment and the power is not used
                var index = Vector256.Max((magnitude.AsInt32() >>> 23) - firstExponent, Vector256<int>.Zero);
                var factor = Avx512F.VL.IsSupported
                    ? Avx512F.VL.PermuteVar8x32x2(lowFactors, index, highFactors)
                    : Avx.BlendVariable(Avx2.PermuteVar8x32(lowFactors, index), Avx2.PermuteVar8x32(highFactors, index), (index << 28).AsSingle());
                Encode(value, magnitude, factor * PowerFiveTwelfthsOfMantissa(magnitude)).StoreUnsafe(ref start, (nuint)i);
            }
        }

        return i;
    }

    /// <summary>The loop of <see cref="EncodeSrgbRunVector256"/> on four samples, for Arm64.</summary>
    private static int EncodeSrgbRunArm64(ref float start, int i, int length)
    {
        var table = ExponentFactors.AsSpan();
        var factors = (Vector128.Create(table[..4]).AsByte(), Vector128.Create(table.Slice(4, 4)).AsByte(), Vector128.Create(table.Slice(8, 4)).AsByte(), Vector128.Create(table.Slice(12, 4)).AsByte());
        var lastTableBits = Vector128.Create(LastTableBits);
        var firstExponent = Vector128.Create(FirstTableExponent);
        var byteSpread = Vector128.Create(0x04040404);
        var byteOffsets = Vector128.Create(0x03020100);
        var mantissaMask = Vector128.Create(MantissaMask);
        var one = Vector128.Create(OneBits);
        var m1 = Vector128.Create(M1);
        var m2 = Vector128.Create(M2);
        var m3 = Vector128.Create(M3);
        var m4 = Vector128.Create(M4);
        var m5 = Vector128.Create(M5);
        var m6 = Vector128.Create(M6);
        var m7 = Vector128.Create(M7);
        var linearLimit = Vector128.Create(SrgbLinearLimit);
        var linearSlope = Vector128.Create(SrgbLinearSlope);
        var scale = Vector128.Create(SrgbScale);
        var offset = Vector128.Create(SrgbOffset);
        for (; i <= length - 4; i += 4)
        {
            unsafe
            {
                var value = Vector128.LoadUnsafe(ref start, (nuint)i);
                var magnitude = Vector128.Abs(value);
                if (Vector128.GreaterThanAny(magnitude.AsInt32(), lastTableBits))
                    return i;

                // Below the table, the sample is in the linear segment and the power is not used; the bytes of the factor
                // of a lane are at 4 i to 4 i + 3
                var index = Vector128.Max((magnitude.AsInt32() >>> 23) - firstExponent, Vector128<int>.Zero);
                var factor = AdvSimd.Arm64.VectorTableLookup(factors, ((index * byteSpread) + byteOffsets).AsByte()).AsSingle();

                // PowerFiveTwelfthsOfMantissa and Encode, with the constants in registers
                var t = ((magnitude.AsInt32() & mantissaMask) | one).AsSingle() - Vector128<float>.One;
                var root = (m7 * t) + m6;
                root = (root * t) + m5;
                root = (root * t) + m4;
                root = (root * t) + m3;
                root = (root * t) + m2;
                root = (root * t) + m1;
                root = (root * t) + Vector128<float>.One;
                var power = factor * root;
                var encoded = Vector128.ConditionalSelect(Vector128.LessThanOrEqual(magnitude, linearLimit), linearSlope * magnitude, (scale * power) - offset);
                Vector128.CopySign(encoded, value).StoreUnsafe(ref start, (nuint)i);
            }
        }

        return i;
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
        return Encode(value, magnitude, power);
    }

    /// <summary>The encoding of a sample from its magnitude and the power of its magnitude, which only the samples above the linear segment use.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<float> Encode(Vector128<float> value, Vector128<float> magnitude, Vector128<float> power)
    {
        var encoded = Vector128.ConditionalSelect(Vector128.LessThanOrEqual(magnitude, Vector128.Create(SrgbLinearLimit)), Vector128.Create(SrgbLinearSlope) * magnitude, (Vector128.Create(SrgbScale) * power) - Vector128.Create(SrgbOffset));
        return Vector128.CopySign(encoded, value);
    }

    private static Vector256<float> EncodeSrgb(Vector256<float> value)
    {
        var magnitude = Vector256.Abs(value);
        var power = Vector256.ConditionalSelect(Vector256.LessThan(magnitude, Vector256.Create(float.PositiveInfinity)), PowerFiveTwelfths(magnitude), magnitude);
        return Encode(value, magnitude, power);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<float> Encode(Vector256<float> value, Vector256<float> magnitude, Vector256<float> power)
    {
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
        var shifted = (Vector128.Create(5) * (bits >>> 23)) + Vector128.Create(ExponentOffset);
        var quotient = (shifted * Vector128.Create(DivideBy12Multiplier)) >> DivideBy12Shift;
        var remainder = Vector128.ConvertToSingle(shifted - (Vector128.Create(12) * quotient));
        var scale = ((quotient + Vector128.Create(63)) << 23).AsSingle();
        var root = PowerFiveTwelfthsOfMantissa(magnitude);
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
        var shifted = (Vector256.Create(5) * (bits >>> 23)) + Vector256.Create(ExponentOffset);
        var quotient = (shifted * Vector256.Create(DivideBy12Multiplier)) >> DivideBy12Shift;
        var remainder = Vector256.ConvertToSingle(shifted - (Vector256.Create(12) * quotient));
        var scale = ((quotient + Vector256.Create(63)) << 23).AsSingle();
        var root = PowerFiveTwelfthsOfMantissa(magnitude);
        var fraction = (Vector256.Create(E5) * remainder) + Vector256.Create(E4);
        fraction = (fraction * remainder) + Vector256.Create(E3);
        fraction = (fraction * remainder) + Vector256.Create(E2);
        fraction = (fraction * remainder) + Vector256.Create(E1);
        fraction = (fraction * remainder) + Vector256.Create(E0);
        return scale * (fraction * root);
    }

    /// <summary>The factor <c>m^(5/12)</c> of <see cref="PowerFiveTwelfths(float)"/>, where <c>m</c> is the mantissa of the magnitude.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<float> PowerFiveTwelfthsOfMantissa(Vector128<float> magnitude)
    {
        var mantissa = ((magnitude.AsInt32() & Vector128.Create(MantissaMask)) | Vector128.Create(OneBits)).AsSingle();
        var t = mantissa - Vector128<float>.One;
        var root = (Vector128.Create(M7) * t) + Vector128.Create(M6);
        root = (root * t) + Vector128.Create(M5);
        root = (root * t) + Vector128.Create(M4);
        root = (root * t) + Vector128.Create(M3);
        root = (root * t) + Vector128.Create(M2);
        root = (root * t) + Vector128.Create(M1);
        return (root * t) + Vector128.Create(M0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector256<float> PowerFiveTwelfthsOfMantissa(Vector256<float> magnitude)
    {
        var mantissa = ((magnitude.AsInt32() & Vector256.Create(MantissaMask)) | Vector256.Create(OneBits)).AsSingle();
        var t = mantissa - Vector256<float>.One;
        var root = (Vector256.Create(M7) * t) + Vector256.Create(M6);
        root = (root * t) + Vector256.Create(M5);
        root = (root * t) + Vector256.Create(M4);
        root = (root * t) + Vector256.Create(M3);
        root = (root * t) + Vector256.Create(M2);
        root = (root * t) + Vector256.Create(M1);
        return (root * t) + Vector256.Create(M0);
    }

    /// <summary>The factors <c>2^n 2^(r/12)</c> of <see cref="PowerFiveTwelfths(float)"/> for the 16 exponents from <see cref="FirstTableExponent"/>.</summary>
    private static float[] CreateExponentFactors()
    {
        var factors = new float[16];
        for (var i = 0; i < factors.Length; i++)
        {
            // The power of a magnitude whose mantissa is 1 is the factor of its exponent times m^(5/12) = M0 = 1
            factors[i] = PowerFiveTwelfths(BitConverter.Int32BitsToSingle((FirstTableExponent + i) << 23));
        }

        return factors;
    }
}
