using System.Runtime.InteropServices;
using SkiaSharp;

namespace Meziantou.Framework.SnapshotTesting.SkiaSharp;

internal sealed class SkiaSharpSnapshotComparer(ImageComparisonSettings? settings) : ISnapshotComparer
{
    private static readonly ImageComparer HighPrecisionComparer = new();

    public bool Equals(SnapshotData expected, SnapshotData actual) => Equals(expected, actual, out _);

    public bool Equals(SnapshotData expected, SnapshotData actual, out string? mismatchReason)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        mismatchReason = null;

        // Identical bytes decode to identical pixels, so an exact comparison matches, SSIM is 1.0 and both
        // hash distances are 0 - every configured threshold is satisfied. This is the case for every passing
        // image snapshot test, and it avoids decoding both images.
        if (expected.Data.AsSpan().SequenceEqual(actual.Data))
            return true;

        using var expectedImage = Decode(expected.Data);
        if (expectedImage is null)
        {
            mismatchReason = ImageMismatchReasons.CannotDecode("expected", error: null);
            return false;
        }

        using var actualImage = Decode(actual.Data);
        if (actualImage is null)
        {
            mismatchReason = ImageMismatchReasons.CannotDecode("actual", error: null);
            return false;
        }

        if (expectedImage.Width != actualImage.Width || expectedImage.Height != actualImage.Height)
        {
            mismatchReason = ImageMismatchReasons.DifferentSizes(expectedImage.Width, expectedImage.Height, actualImage.Width, actualImage.Height);
            return false;
        }

        var expectedPixels = unsafe(MemoryMarshal.Cast<byte, uint>(expectedImage.GetPixelSpan()));
        var actualPixels = unsafe(MemoryMarshal.Cast<byte, uint>(actualImage.GetPixelSpan()));

        var threshold = settings?.SimilarityThreshold;
        if (threshold is null)
        {
            if (!ExactEquals(expectedPixels, actualPixels))
            {
                mismatchReason = ImageMismatchReasons.DifferentPixels;
                return false;
            }

            // Skia decodes 16-bit PNG samples to their high byte, and none of the color types it decodes them to
            // keeps the 16 bits exactly (half floats lose the low bits). The low bytes are compared with the
            // built-in decoder, so an exact comparison sees every bit, as with the built-in comparer and ImageSharp.
            if (IsSixteenBitPng(expected.Data) || IsSixteenBitPng(actual.Data))
                return HighPrecisionComparer.Equals(expected, actual, out mismatchReason);

            return true;
        }

        // The bitmaps are allocated here, so their rows are contiguous and tightly packed. The SSIM works on the
        // 8-bit samples, and Skia reduces 16-bit samples to their high byte, as the built-in comparer and ImageSharp do.
        var ssim = SsimAccumulator.Compute(expectedPixels, actualPixels, expectedImage.Width, expectedImage.Height);
        if (ssim >= threshold.Value)
            return true;

        mismatchReason = ImageMismatchReasons.SimilarityBelowThreshold(ssim, threshold.Value);
        return false;
    }

    /// <summary>
    /// Reads the bit depth from the IHDR chunk, which the PNG specification requires to come first.
    /// </summary>
    private static bool IsSixteenBitPng(ReadOnlySpan<byte> data)
    {
        return data.Length > 24
            && data.StartsWith((ReadOnlySpan<byte>)[137, 80, 78, 71, 13, 10, 26, 10])
            && data.Slice(12, 4).SequenceEqual("IHDR"u8)
            && data[24] is 16;
    }

    private static bool ExactEquals(ReadOnlySpan<uint> expected, ReadOnlySpan<uint> actual)
    {
        if (expected.SequenceEqual(actual))
            return true;

        // Encoders store arbitrary color values under a zero alpha, so fully transparent pixels are equal
        // whatever color they hide. The alpha is the high byte of an RGBA pixel read as a little-endian uint.
        for (var i = 0; i < expected.Length; i++)
        {
            if (expected[i] != actual[i] && (expected[i] >> 24 is not 0 || actual[i] >> 24 is not 0))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Decodes the image into a tightly packed <see cref="SKColorType.Rgba8888"/> / <see cref="SKAlphaType.Unpremul"/>
    /// bitmap so both snapshots share the same memory layout regardless of the encoded format. Returns
    /// <see langword="null"/> when the image cannot be decoded, including when its header announces dimensions too
    /// large to allocate.
    /// </summary>
    private static SKBitmap? Decode(byte[] data)
    {
        using var skData = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(skData);
        if (codec is null)
            return null;

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

        // The pixels are exposed as a span, whose length is limited to int.MaxValue
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height * info.BytesPerPixel > Array.MaxLength)
            return null;

        SKBitmap? bitmap = null;
        try
        {
            bitmap = new SKBitmap(info);

            // SkiaSharp does not throw when it cannot allocate the pixels; the bitmap has no pixels instead
            if (bitmap.GetPixels() == IntPtr.Zero || codec.GetPixels(info, bitmap.GetPixels()) is not SKCodecResult.Success)
                return null;

            var result = bitmap;
            bitmap = null;
            return result;
        }
        catch (OutOfMemoryException)
        {
            return null;
        }
        finally
        {
            bitmap?.Dispose();
        }
    }
}
