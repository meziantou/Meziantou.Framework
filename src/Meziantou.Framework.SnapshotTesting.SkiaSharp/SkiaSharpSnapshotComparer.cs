using System.Runtime.InteropServices;
using SkiaSharp;

namespace Meziantou.Framework.SnapshotTesting.SkiaSharp;

internal sealed class SkiaSharpSnapshotComparer(ImageComparisonSettings? settings) : ISnapshotComparer
{
    public bool Equals(SnapshotData expected, SnapshotData actual)
    {
        // Identical bytes decode to identical pixels, so an exact comparison matches, SSIM is 1.0 and both
        // hash distances are 0 - every configured threshold is satisfied. This is the case for every passing
        // image snapshot test, and it avoids decoding both images.
        if (expected.Data.AsSpan().SequenceEqual(actual.Data))
            return true;

        using var expectedImage = Decode(expected.Data);
        using var actualImage = Decode(actual.Data);
        if (expectedImage is null || actualImage is null)
            return false;

        if (expectedImage.Width != actualImage.Width || expectedImage.Height != actualImage.Height)
            return false;

        var expectedPixels = MemoryMarshal.Cast<byte, uint>(expectedImage.GetPixelSpan());
        var actualPixels = MemoryMarshal.Cast<byte, uint>(actualImage.GetPixelSpan());

        var threshold = settings?.SimilarityThreshold;
        if (threshold is null)
            return expectedPixels.SequenceEqual(actualPixels);

        // The bitmaps are allocated here, so their rows are contiguous and the whole image is a single chunk
        var accumulator = new SsimAccumulator();
        accumulator.Add(expectedPixels, actualPixels);
        return accumulator.ComputeMeanSsim() >= threshold.Value;
    }

    /// <summary>
    /// Decodes the image into a tightly packed <see cref="SKColorType.Rgba8888"/> / <see cref="SKAlphaType.Unpremul"/>
    /// bitmap so both snapshots share the same memory layout regardless of the encoded format.
    /// </summary>
    private static SKBitmap? Decode(byte[] data)
    {
        using var skData = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(skData);
        if (codec is null)
            return null;

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        if (codec.GetPixels(info, bitmap.GetPixels()) is not SKCodecResult.Success)
        {
            bitmap.Dispose();
            return null;
        }

        return bitmap;
    }
}
