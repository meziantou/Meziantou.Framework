using System.Runtime.InteropServices;

namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// Compares BMP/PNG/JPEG/TIFF snapshots by decoding image pixels and comparing them exactly, or by similarity when
/// <see cref="ImageComparisonSettings"/> configures a threshold.
/// </summary>
public sealed class ImageComparer(ImageComparisonSettings? settings = null) : ISnapshotComparer
{
    internal static ImageComparer Instance { get; } = new();

    public bool Equals(SnapshotData expected, SnapshotData actual) => Equals(expected, actual, out _);

    /// <inheritdoc/>
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

        // The bytes differ and a snapshot that is not a decodable image does not match
        if (!TryLoad(expected, out var expectedImage, out var expectedError))
        {
            mismatchReason = ImageMismatchReasons.CannotDecode("expected", expectedError);
            return false;
        }

        if (!TryLoad(actual, out var actualImage, out var actualError))
        {
            mismatchReason = ImageMismatchReasons.CannotDecode("actual", actualError);
            return false;
        }

        var similarityThreshold = settings?.SimilarityThreshold;
        var dHashThreshold = settings?.DHashThreshold;
        var pHashThreshold = settings?.PHashThreshold;
        if (expectedImage.Width != actualImage.Width || expectedImage.Height != actualImage.Height)
        {
            mismatchReason = ImageMismatchReasons.DifferentSizes(expectedImage.Width, expectedImage.Height, actualImage.Width, actualImage.Height);
            return false;
        }

        if (similarityThreshold is null && dHashThreshold is null && pHashThreshold is null)
        {
            if (expectedImage.Equals(actualImage))
                return true;

            mismatchReason = ImageMismatchReasons.DifferentPixels;
            return false;
        }

        // Every configured check is evaluated, so the message reports all the ones that failed
        List<string>? reasons = null;
        if (similarityThreshold is not null)
        {
            var ssim = SsimAccumulator.Compute(
                MemoryMarshal.Cast<Argb, uint>(expectedImage.Pixels.Span),
                MemoryMarshal.Cast<Argb, uint>(actualImage.Pixels.Span),
                expectedImage.Width,
                expectedImage.Height);
            if (ssim < similarityThreshold.Value)
            {
                (reasons ??= []).Add(ImageMismatchReasons.SimilarityBelowThreshold(ssim, similarityThreshold.Value));
            }
        }

        if (dHashThreshold is not null)
        {
            var distance = ImageHash.ComputeDHashDistance(expectedImage, actualImage);
            if (distance > dHashThreshold.Value)
            {
                (reasons ??= []).Add(string.Create(CultureInfo.InvariantCulture, $"The dHash distance {distance} is above the threshold {dHashThreshold.Value}."));
            }
        }

        if (pHashThreshold is not null)
        {
            var distance = ImageHash.ComputePHashDistance(expectedImage, actualImage);
            if (distance > pHashThreshold.Value)
            {
                (reasons ??= []).Add(string.Create(CultureInfo.InvariantCulture, $"The pHash distance {distance} is above the threshold {pHashThreshold.Value}."));
            }
        }

        if (reasons is null)
            return true;

        mismatchReason = string.Join(' ', reasons);
        return false;
    }

    private static bool TryLoad(SnapshotData snapshot, [NotNullWhen(true)] out Image? image, [NotNullWhen(false)] out Exception? error)
    {
        try
        {
            image = Image.Load(snapshot.Data);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            image = null;
            error = ex;
            return false;
        }
    }
}
