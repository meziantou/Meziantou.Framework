using System.Numerics;
using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.PixelFormats;

namespace Meziantou.Framework.SnapshotTesting.ImageSharp;

internal sealed class ImageSharpSnapshotComparer(ImageComparisonSettings? settings) : ISnapshotComparer
{
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

        // Samples of more than 8 bits (16-bit PNG, TIFF, ...) are decoded without losing precision, so an exact
        // comparison sees every bit, as with the built-in ImageComparer. Other images are decoded as Rgba32, which
        // is lossless for them. An 8-bit sample v is the 16-bit sample v × 257, so mixed images compare exactly too.
        if (HasHighPrecisionSamples(expected.Data) || HasHighPrecisionSamples(actual.Data))
            return Compare<Rgba64>(expected, actual, out mismatchReason);

        return Compare<Rgba32>(expected, actual, out mismatchReason);
    }

    private bool Compare<TPixel>(SnapshotData expected, SnapshotData actual, out string? mismatchReason)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        mismatchReason = null;
        using var expectedImage = Decode<TPixel>(expected.Data, out var expectedError);
        if (expectedImage is null)
        {
            mismatchReason = ImageMismatchReasons.CannotDecode("expected", expectedError);
            return false;
        }

        using var actualImage = Decode<TPixel>(actual.Data, out var actualError);
        if (actualImage is null)
        {
            mismatchReason = ImageMismatchReasons.CannotDecode("actual", actualError);
            return false;
        }

        if (expectedImage.Width != actualImage.Width || expectedImage.Height != actualImage.Height)
        {
            mismatchReason = ImageMismatchReasons.DifferentSizes(expectedImage.Width, expectedImage.Height, actualImage.Width, actualImage.Height);
            return false;
        }

        var threshold = settings?.SimilarityThreshold;
        if (threshold is null)
        {
            if (ExactEquals(expectedImage, actualImage))
                return true;

            mismatchReason = ImageMismatchReasons.DifferentPixels;
            return false;
        }

        var ssim = ComputeMeanSsim(expectedImage, actualImage);
        if (ssim >= threshold.Value)
            return true;

        mismatchReason = ImageMismatchReasons.SimilarityBelowThreshold(ssim, threshold.Value);
        return false;
    }

    private static bool HasHighPrecisionSamples(byte[] data)
    {
        try
        {
            return Image.Identify(data).PixelType.ComponentInfo?.GetMaximumComponentPrecision() > 8;
        }
        catch (Exception ex) when (ex is ImageFormatException or NotSupportedException or InvalidMemoryOperationException or OutOfMemoryException)
        {
            // Decoding reports the error
            return false;
        }
    }

    /// <summary>
    /// Decodes a snapshot, or returns <see langword="null"/> when it cannot be decoded. A snapshot that cannot be
    /// decoded is a snapshot that does not match. Letting the exception escape would report a corrupt verified file
    /// as a library crash instead of a mismatch, which is what the built-in ImageComparer and
    /// SkiaSharpSnapshotComparer already do.
    /// </summary>
    private static Image<TPixel>? Decode<TPixel>(byte[] data, out Exception? error)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        try
        {
            error = null;
            return Image.Load<TPixel>(data);
        }
        catch (Exception ex) when (ex is ImageFormatException or NotSupportedException or InvalidMemoryOperationException or OutOfMemoryException)
        {
            // InvalidMemoryOperationException: the header announces dimensions larger than the memory allocator accepts
            error = ex;
            return null;
        }
    }

    private static bool ExactEquals<TPixel>(Image<TPixel> expected, Image<TPixel> actual)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var equal = true;
        expected.ProcessPixelRows(actual, (expectedAccessor, actualAccessor) =>
        {
            for (var y = 0; y < expectedAccessor.Height && equal; y++)
            {
                if (!RowEquals(expectedAccessor.GetRowSpan(y), actualAccessor.GetRowSpan(y)))
                    equal = false;
            }
        });
        return equal;
    }

    private static bool RowEquals<TPixel>(Span<TPixel> expected, Span<TPixel> actual)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        // Rgba32 is four bytes and Rgba64 four ushorts, without padding, so their uint and ulong views are exact
        return typeof(TPixel) == typeof(Rgba64)
            ? RowEquals(unsafe(MemoryMarshal.Cast<TPixel, ulong>(expected)), unsafe(MemoryMarshal.Cast<TPixel, ulong>(actual)), alphaShift: 48)
            : RowEquals(unsafe(MemoryMarshal.Cast<TPixel, uint>(expected)), unsafe(MemoryMarshal.Cast<TPixel, uint>(actual)), alphaShift: 24);
    }

    private static bool RowEquals<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, int alphaShift)
        where T : unmanaged, IBinaryInteger<T>
    {
        if (expected.SequenceEqual(actual))
            return true;

        // Encoders store arbitrary color values under a zero alpha, so fully transparent pixels are equal
        // whatever color they hide. The alpha is the highest sample of the pixel read as a little-endian integer.
        for (var i = 0; i < expected.Length; i++)
        {
            if (expected[i] != actual[i] && (expected[i] >>> alphaShift != T.Zero || actual[i] >>> alphaShift != T.Zero))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Computes the mean Structural Similarity Index (SSIM) of the images. ImageSharp does not guarantee that an
    /// image is backed by a single buffer, so the rows are accumulated one at a time. The SSIM works on 8-bit
    /// samples: 16-bit samples keep their high byte, as the built-in ImageComparer and SkiaSharp reduce them.
    /// </summary>
    private static double ComputeMeanSsim<TPixel>(Image<TPixel> expected, Image<TPixel> actual)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        var accumulator = new SsimAccumulator(expected.Width, expected.Height);
        var isHighPrecision = typeof(TPixel) == typeof(Rgba64);
        var expectedRow = isHighPrecision ? new uint[expected.Width] : null;
        var actualRow = isHighPrecision ? new uint[expected.Width] : null;
        expected.ProcessPixelRows(actual, (expectedAccessor, actualAccessor) =>
        {
            for (var y = 0; y < expectedAccessor.Height; y++)
            {
                if (expectedRow is not null && actualRow is not null)
                {
                    ReduceTo8Bits(unsafe(MemoryMarshal.Cast<TPixel, Rgba64>(expectedAccessor.GetRowSpan(y))), expectedRow);
                    ReduceTo8Bits(unsafe(MemoryMarshal.Cast<TPixel, Rgba64>(actualAccessor.GetRowSpan(y))), actualRow);
                    accumulator.AddRow(expectedRow, actualRow);
                }
                else
                {
                    accumulator.AddRow(
                        unsafe(MemoryMarshal.Cast<TPixel, uint>(expectedAccessor.GetRowSpan(y))),
                        unsafe(MemoryMarshal.Cast<TPixel, uint>(actualAccessor.GetRowSpan(y))));
                }
            }
        });

        return accumulator.ComputeMeanSsim();
    }

    /// <summary>
    /// Packs each pixel as an Rgba32 read as a little-endian uint, keeping the high byte of every sample.
    /// </summary>
    private static void ReduceTo8Bits(ReadOnlySpan<Rgba64> source, Span<uint> destination)
    {
        for (var i = 0; i < source.Length; i++)
        {
            var pixel = source[i];
            destination[i] = (uint)(pixel.R >> 8) | (uint)(pixel.G >> 8) << 8 | (uint)(pixel.B >> 8) << 16 | (uint)(pixel.A >> 8) << 24;
        }
    }
}
