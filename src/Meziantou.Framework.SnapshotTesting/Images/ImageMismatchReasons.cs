namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// The descriptions of image mismatches shared by the built-in, ImageSharp and SkiaSharp comparers, so the three
/// backends word the same failure the same way.
/// </summary>
internal static class ImageMismatchReasons
{
    public const string DifferentPixels = "The images have different pixels.";

    /// <param name="snapshot"><c>expected</c> or <c>actual</c>.</param>
    public static string CannotDecode(string snapshot, Exception? error)
    {
        return error is null
            ? $"The {snapshot} snapshot cannot be decoded as an image."
            : $"The {snapshot} snapshot cannot be decoded as an image: {error.Message}";
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
}
