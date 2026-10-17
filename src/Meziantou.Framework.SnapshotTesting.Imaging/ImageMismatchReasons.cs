namespace Meziantou.Framework.SnapshotTesting.Imaging;

/// <summary>The descriptions of image mismatches, shown under the changed file in the assertion message.</summary>
internal static class ImageMismatchReasons
{
    public const string DifferentPixels = "The images have different pixels.";

    /// <param name="snapshot"><c>expected</c> or <c>actual</c>.</param>
    public static string CannotDecode(string snapshot, Exception error)
    {
        return $"The {snapshot} snapshot cannot be decoded as an image: {error.Message}";
    }

    public static string DifferentFrameCounts(int expectedCount, int actualCount)
    {
        return string.Create(CultureInfo.InvariantCulture, $"The images have a different number of frames: expected {expectedCount}, actual {actualCount}.");
    }

    public static string DifferentSizes(int expectedWidth, int expectedHeight, int actualWidth, int actualHeight)
    {
        return string.Create(CultureInfo.InvariantCulture, $"The images have different sizes: expected {expectedWidth}x{expectedHeight}, actual {actualWidth}x{actualHeight}.");
    }

    public static string SimilarityBelowThreshold(double score, float threshold)
    {
        // Rounded down, so a score just below the threshold is never displayed as equal to it
        var displayedScore = Math.Floor(score * 1_000_000) / 1_000_000;
        return string.Create(CultureInfo.InvariantCulture, $"The SSIM score {displayedScore:0.######} is below the threshold {threshold}.");
    }

    public static string DHashAboveThreshold(int distance, int threshold)
    {
        return string.Create(CultureInfo.InvariantCulture, $"The dHash distance {distance} is above the threshold {threshold}.");
    }

    public static string PHashAboveThreshold(int distance, int threshold)
    {
        return string.Create(CultureInfo.InvariantCulture, $"The pHash distance {distance} is above the threshold {threshold}.");
    }

    /// <summary>Prefixes the reason of a mismatch with the frame it was found in, when the images have several frames.</summary>
    public static string InFrame(int index, int count, string reason)
    {
        return count is 1 ? reason : string.Create(CultureInfo.InvariantCulture, $"Frame {index + 1} of {count}: {reason}");
    }
}
