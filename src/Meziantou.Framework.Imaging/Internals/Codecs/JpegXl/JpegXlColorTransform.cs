namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The color transforms that end the decoding of a frame and do not depend on the image (ISO/IEC 18181-1, annex L): from
/// YCbCr to RGB samples where 1 is the largest value of an integer sample, and the sRGB transfer function.
/// </summary>
internal static class JpegXlColorTransform
{
    /// <summary>
    /// Converts rows of YCbCr samples as JPEG defines them (full range, chroma centered on zero, luma centered on one half)
    /// to RGB samples, in place.
    /// </summary>
    /// <param name="cb">The Cb samples, replaced with the red ones.</param>
    /// <param name="y">The Y samples, replaced with the green ones.</param>
    /// <param name="cr">The Cr samples, replaced with the blue ones.</param>
    public static void YCbCrToRgb(Span<float> cb, Span<float> y, Span<float> cr)
    {
        const float Offset = 128f / 255;
        const float RedFromCr = 1.402f;
        const float GreenFromCb = -0.114f * 1.772f / 0.587f;
        const float GreenFromCr = -0.299f * 1.402f / 0.587f;
        const float BlueFromCb = 1.772f;
        for (var i = 0; i < y.Length; i++)
        {
            var luma = y[i] + Offset;
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
