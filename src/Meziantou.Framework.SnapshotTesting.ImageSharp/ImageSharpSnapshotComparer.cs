using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Memory;
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

        using var expectedImage = Decode(expected.Data);
        using var actualImage = Decode(actual.Data);
        if (expectedImage is null || actualImage is null)
            return false;

        if (expectedImage.Width != actualImage.Width || expectedImage.Height != actualImage.Height)
            return false;

        var threshold = settings?.SimilarityThreshold;
        if (threshold is null)
            return ExactEquals(expectedImage, actualImage);

        return ComputeMeanSsim(expectedImage, actualImage) >= threshold.Value;
    }

    /// <summary>
    /// Decodes a snapshot, or returns <see langword="null"/> when it cannot be decoded. A snapshot that cannot be
    /// decoded is a snapshot that does not match. Letting the exception escape would report a corrupt verified file
    /// as a library crash instead of a mismatch, which is what the built-in ImageComparer and
    /// SkiaSharpSnapshotComparer already do.
    /// </summary>
    private static Image<Rgba32>? Decode(byte[] data)
    {
        try
        {
            return Image.Load<Rgba32>(data);
        }
        catch (ImageFormatException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (InvalidMemoryOperationException)
        {
            // The header announces dimensions larger than the memory allocator accepts
            return null;
        }
        catch (OutOfMemoryException)
        {
            return null;
        }
    }

    private static bool ExactEquals(Image<Rgba32> expected, Image<Rgba32> actual)
    {
        var equal = true;
        expected.ProcessPixelRows(actual, (expectedAccessor, actualAccessor) =>
        {
            for (var y = 0; y < expectedAccessor.Height && equal; y++)
            {
                // Rgba32 is four bytes without padding, so its byte and uint views are exact.
                var expectedRow = unsafe(MemoryMarshal.Cast<Rgba32, uint>(expectedAccessor.GetRowSpan(y)));
                var actualRow = unsafe(MemoryMarshal.Cast<Rgba32, uint>(actualAccessor.GetRowSpan(y)));
                if (!RowEquals(expectedRow, actualRow))
                    equal = false;
            }
        });
        return equal;
    }

    private static bool RowEquals(ReadOnlySpan<uint> expected, ReadOnlySpan<uint> actual)
    {
        if (expected.SequenceEqual(actual))
            return true;

        // Encoders store arbitrary color values under a zero alpha, so fully transparent pixels are equal
        // whatever color they hide. The alpha is the high byte of an Rgba32 read as a little-endian uint.
        for (var i = 0; i < expected.Length; i++)
        {
            if (expected[i] != actual[i] && (expected[i] >> 24 is not 0 || actual[i] >> 24 is not 0))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Computes the mean Structural Similarity Index (SSIM) of the images. ImageSharp does not guarantee that an
    /// image is backed by a single buffer, so the rows are accumulated one at a time.
    /// </summary>
    private static double ComputeMeanSsim(Image<Rgba32> expected, Image<Rgba32> actual)
    {
        var accumulator = new SsimAccumulator(expected.Width, expected.Height);
        expected.ProcessPixelRows(actual, (expectedAccessor, actualAccessor) =>
        {
            for (var y = 0; y < expectedAccessor.Height; y++)
            {
                accumulator.AddRow(
                    unsafe(MemoryMarshal.Cast<Rgba32, uint>(expectedAccessor.GetRowSpan(y))),
                    unsafe(MemoryMarshal.Cast<Rgba32, uint>(actualAccessor.GetRowSpan(y))));
            }
        });

        return accumulator.ComputeMeanSsim();
    }
}
