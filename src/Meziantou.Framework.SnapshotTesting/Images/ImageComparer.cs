using System.Runtime.InteropServices;

namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// Compares BMP/PNG/JPEG/TIFF snapshots by decoding image pixels and comparing RGB similarity.
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

        try
        {
            var expectedImage = Image.Load(expected.Data);
            var actualImage = Image.Load(actual.Data);

            var similarityThreshold = settings?.SimilarityThreshold;
            var dHashThreshold = settings?.DHashThreshold;
            var pHashThreshold = settings?.PHashThreshold;
            if (similarityThreshold is null && dHashThreshold is null && pHashThreshold is null)
                return expectedImage.Equals(actualImage);

            if (similarityThreshold is not null)
            {
                if (expectedImage.Width != actualImage.Width || expectedImage.Height != actualImage.Height)
                    return false;

                var accumulator = new SsimAccumulator();
                accumulator.Add(MemoryMarshal.Cast<Argb, uint>(expectedImage.Pixels.Span), MemoryMarshal.Cast<Argb, uint>(actualImage.Pixels.Span));
                if (accumulator.ComputeMeanSsim() < similarityThreshold.Value)
                    return false;
            }

            if (dHashThreshold is not null)
            {
                var expectedHash = ImageHash.ComputeDHash(expectedImage);
                var actualHash = ImageHash.ComputeDHash(actualImage);
                if (ImageHash.ComputeHammingDistance(expectedHash, actualHash) > dHashThreshold.Value)
                    return false;
            }

            if (pHashThreshold is not null)
            {
                var expectedHash = ImageHash.ComputePHash(expectedImage);
                var actualHash = ImageHash.ComputePHash(actualImage);
                if (ImageHash.ComputeHammingDistance(expectedHash, actualHash) > pHashThreshold.Value)
                    return false;
            }

            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}
