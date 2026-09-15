using System.Runtime.InteropServices;

namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// Compares BMP/PNG/JPEG/TIFF snapshots by decoding image pixels and comparing them exactly, or by similarity when
/// <see cref="ImageComparisonSettings"/> configures a threshold.
/// </summary>
public sealed class ImageComparer(ImageComparisonSettings? settings = null) : ISnapshotComparer
{
    internal static ImageComparer Instance { get; } = new();

    public bool Equals(SnapshotData expected, SnapshotData actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        // Identical bytes decode to identical pixels, so an exact comparison matches, SSIM is 1.0 and both
        // hash distances are 0 - every configured threshold is satisfied. This is the case for every passing
        // image snapshot test, and it avoids decoding both images.
        if (expected.Data.AsSpan().SequenceEqual(actual.Data))
            return true;

        Image expectedImage;
        Image actualImage;
        try
        {
            expectedImage = Image.Load(expected.Data);
            actualImage = Image.Load(actual.Data);
        }
        catch (InvalidDataException)
        {
            // The bytes differ and at least one snapshot is not a decodable image, so they do not match
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }

        var similarityThreshold = settings?.SimilarityThreshold;
        var dHashThreshold = settings?.DHashThreshold;
        var pHashThreshold = settings?.PHashThreshold;
        if (similarityThreshold is null && dHashThreshold is null && pHashThreshold is null)
            return expectedImage.Equals(actualImage);

        if (expectedImage.Width != actualImage.Width || expectedImage.Height != actualImage.Height)
            return false;

        if (similarityThreshold is not null)
        {
            var ssim = SsimAccumulator.Compute(
                MemoryMarshal.Cast<Argb, uint>(expectedImage.Pixels.Span),
                MemoryMarshal.Cast<Argb, uint>(actualImage.Pixels.Span),
                expectedImage.Width,
                expectedImage.Height);
            if (ssim < similarityThreshold.Value)
                return false;
        }

        if (dHashThreshold is not null && ImageHash.ComputeDHashDistance(expectedImage, actualImage) > dHashThreshold.Value)
            return false;

        if (pHashThreshold is not null && ImageHash.ComputePHashDistance(expectedImage, actualImage) > pHashThreshold.Value)
            return false;

        return true;
    }
}
