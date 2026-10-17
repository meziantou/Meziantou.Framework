using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.SnapshotTesting.Imaging;

/// <summary>
/// Compares image snapshots by their pixels, exactly or by similarity when <see cref="ImageComparisonSettings"/> sets a
/// threshold. Every format <c>Meziantou.Framework.Imaging</c> decodes is supported. Every frame is compared: the frames of
/// an animation, the pages of a TIFF file and the representations of an ICO or CUR file.
/// </summary>
public sealed class ImageComparer(ImageComparisonSettings? settings = null) : ISnapshotComparer
{
    internal static ImageComparer Instance { get; } = new();

    /// <inheritdoc/>
    public bool Equals(SnapshotData expected, SnapshotData actual) => Equals(expected, actual, out _);

    /// <inheritdoc/>
    public bool Equals(SnapshotData expected, SnapshotData actual, out string? mismatchReason)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        mismatchReason = null;

        // Identical bytes decode to identical pixels, so an exact comparison matches, SSIM is 1.0 and both hash distances
        // are 0 - every configured threshold is satisfied. This is the case for every passing image snapshot test, and it
        // avoids decoding both images.
        if (expected.Data.AsSpan().SequenceEqual(actual.Data))
            return true;

        // The bytes differ and a snapshot that is not a decodable image does not match
        using var expectedImage = TryLoad(expected, "expected", ref mismatchReason);
        if (expectedImage is null)
            return false;

        using var actualImage = TryLoad(actual, "actual", ref mismatchReason);
        if (actualImage is null)
            return false;

        var frameCount = expectedImage.Frames.Count;
        if (frameCount != actualImage.Frames.Count)
        {
            mismatchReason = ImageMismatchReasons.DifferentFrameCounts(frameCount, actualImage.Frames.Count);
            return false;
        }

        for (var i = 0; i < frameCount; i++)
        {
            var expectedFrame = expectedImage.Frames[i];
            var actualFrame = actualImage.Frames[i];
            if (expectedFrame.Size != actualFrame.Size)
            {
                mismatchReason = ImageMismatchReasons.InFrame(i, frameCount, ImageMismatchReasons.DifferentSizes(expectedFrame.Width, expectedFrame.Height, actualFrame.Width, actualFrame.Height));
                return false;
            }
        }

        if (settings is null || (settings.SimilarityThreshold is null && settings.DHashThreshold is null && settings.PHashThreshold is null))
        {
            // An 8-bit sample v is the 16-bit sample v × 257, so an 8-bit image equals a 16-bit one only when every 16-bit
            // sample is exactly that
            var index = expectedImage.HasHighPrecisionSamples || actualImage.HasHighPrecisionSamples
                ? IndexOfDifferentFrame<Rgba64>(expectedImage, actualImage)
                : IndexOfDifferentFrame<Rgba32>(expectedImage, actualImage);
            if (index < 0)
                return true;

            mismatchReason = ImageMismatchReasons.InFrame(index, frameCount, ImageMismatchReasons.DifferentPixels);
            return false;
        }

        for (var i = 0; i < frameCount; i++)
        {
            var reasons = GetSimilarityMismatches(settings, expectedImage.Frames[i], actualImage.Frames[i]);
            if (reasons is not null)
            {
                mismatchReason = ImageMismatchReasons.InFrame(i, frameCount, string.Join(' ', reasons));
                return false;
            }
        }

        return true;
    }

    /// <summary>Evaluates every configured check, so the message reports all the ones that failed.</summary>
    private static List<string>? GetSimilarityMismatches(ImageComparisonSettings settings, ImageFrame expected, ImageFrame actual)
    {
        List<string>? reasons = null;
        if (settings.SimilarityThreshold is { } similarityThreshold)
        {
            var ssim = ImageSimilarity.ComputeSsim(expected, actual);
            if (ssim < similarityThreshold)
            {
                (reasons ??= []).Add(ImageMismatchReasons.SimilarityBelowThreshold(ssim, similarityThreshold));
            }
        }

        if (settings.DHashThreshold is { } dHashThreshold)
        {
            var distance = ImageSimilarity.ComputeDifferenceHashDistance(expected, actual);
            if (distance > dHashThreshold)
            {
                (reasons ??= []).Add(ImageMismatchReasons.DHashAboveThreshold(distance, dHashThreshold));
            }
        }

        if (settings.PHashThreshold is { } pHashThreshold)
        {
            var distance = ImageSimilarity.ComputePerceptualHashDistance(expected, actual);
            if (distance > pHashThreshold)
            {
                (reasons ??= []).Add(ImageMismatchReasons.PHashAboveThreshold(distance, pHashThreshold));
            }
        }

        return reasons;
    }

    /// <summary>Gets the index of the first frame whose pixels differ, or -1. Fully transparent pixels are equal whatever color they hide.</summary>
    private static int IndexOfDifferentFrame<TPixel>(DecodedSnapshot expected, DecodedSnapshot actual)
        where TPixel : unmanaged, IEquatable<TPixel>
    {
        using var expectedPixels = expected.ConvertTo<TPixel>();
        using var actualPixels = actual.ConvertTo<TPixel>();
        for (var i = 0; i < expectedPixels.Frames.Count; i++)
        {
            var expectedFrame = (ImageFrame<TPixel>)expectedPixels.Frames[i];
            var actualFrame = (ImageFrame<TPixel>)actualPixels.Frames[i];
            var expectedData = new TPixel[checked(expectedFrame.Width * expectedFrame.Height)];
            var actualData = new TPixel[expectedData.Length];
            expectedFrame.CopyPixelDataTo(expectedData);
            actualFrame.CopyPixelDataTo(actualData);
            for (var j = 0; j < expectedData.Length; j++)
            {
                if (!NormalizeTransparentPixel(expectedData[j]).Equals(NormalizeTransparentPixel(actualData[j])))
                    return i;
            }
        }

        return -1;
    }

    private static TPixel NormalizeTransparentPixel<TPixel>(TPixel pixel)
        where TPixel : unmanaged
    {
        return pixel switch
        {
            Rgba32 { A: 0 } => default,
            Rgba64 { A: 0 } => default,
            _ => pixel,
        };
    }

    private static DecodedSnapshot? TryLoad(SnapshotData snapshot, string name, ref string? mismatchReason)
    {
        try
        {
            return DecodedSnapshot.Load(snapshot.Data);
        }
        catch (Exception ex) when (ex is ImageException or NotSupportedException)
        {
            mismatchReason = ImageMismatchReasons.CannotDecode(name, ex);
            return null;
        }
    }
}
