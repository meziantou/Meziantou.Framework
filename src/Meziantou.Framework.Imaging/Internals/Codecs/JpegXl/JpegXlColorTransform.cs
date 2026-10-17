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

    /// <summary>Applies the sRGB transfer function to linear samples, in place; a negative sample is the opposite of the encoding of its opposite.</summary>
    public static void LinearToSrgb(Span<float> samples)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            var value = samples[i];
            var magnitude = MathF.Abs(value);
            var encoded = magnitude <= 0.0031308f ? 12.92f * magnitude : (1.055f * MathF.Pow(magnitude, 1f / 2.4f)) - 0.055f;
            samples[i] = MathF.CopySign(encoded, value);
        }
    }
}
