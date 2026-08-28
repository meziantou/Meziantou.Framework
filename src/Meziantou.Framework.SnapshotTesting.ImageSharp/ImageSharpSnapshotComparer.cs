using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Meziantou.Framework.SnapshotTesting.ImageSharp;

internal sealed class ImageSharpSnapshotComparer(ImageComparisonSettings? settings) : ISnapshotComparer
{
    public bool Equals(SnapshotData expected, SnapshotData actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        // Identical bytes decode to identical pixels, so an exact comparison matches, SSIM is 1.0 and both
        // hash distances are 0 - every configured threshold is satisfied. This is the case for every passing
        // image snapshot test, and it avoids decoding both images.
        if (expected.Data.AsSpan().SequenceEqual(actual.Data))
            return true;

        try
        {
            using var expectedImage = Image.Load<Rgba32>(expected.Data);
            using var actualImage = Image.Load<Rgba32>(actual.Data);

            if (expectedImage.Width != actualImage.Width || expectedImage.Height != actualImage.Height)
                return false;

            var threshold = settings?.SimilarityThreshold;
            if (threshold is null)
                return ExactEquals(expectedImage, actualImage);

            return ComputeMeanSsim(expectedImage, actualImage) >= threshold.Value;
        }
        catch (ImageFormatException)
        {
            // A snapshot that cannot be decoded is a snapshot that does not match. Letting the exception
            // escape would report a corrupt verified file as a library crash instead of a mismatch, which is
            // what the built-in ImageComparer and SkiaSharpSnapshotComparer already do.
            return false;
        }
    }

    private static bool ExactEquals(Image<Rgba32> expected, Image<Rgba32> actual)
    {
        var equal = true;
        expected.ProcessPixelRows(actual, (expectedAccessor, actualAccessor) =>
        {
            for (var y = 0; y < expectedAccessor.Height && equal; y++)
            {
                var expectedRow = MemoryMarshal.AsBytes(expectedAccessor.GetRowSpan(y));
                var actualRow = MemoryMarshal.AsBytes(actualAccessor.GetRowSpan(y));
                if (!expectedRow.SequenceEqual(actualRow))
                    equal = false;
            }
        });
        return equal;
    }

    /// <summary>
    /// Computes the mean Structural Similarity Index (SSIM) across R, G, B channels. ImageSharp does not
    /// guarantee that an image is backed by a single buffer, so the rows are accumulated one at a time.
    /// </summary>
    private static float ComputeMeanSsim(Image<Rgba32> expected, Image<Rgba32> actual)
    {
        var accumulator = new SsimAccumulator();
        expected.ProcessPixelRows(actual, (expectedAccessor, actualAccessor) =>
        {
            for (var y = 0; y < expectedAccessor.Height; y++)
            {
                accumulator.Add(
                    MemoryMarshal.Cast<Rgba32, uint>(expectedAccessor.GetRowSpan(y)),
                    MemoryMarshal.Cast<Rgba32, uint>(actualAccessor.GetRowSpan(y)));
            }
        });

        return accumulator.ComputeMeanSsim();
    }
}
