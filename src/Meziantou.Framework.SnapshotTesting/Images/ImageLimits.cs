namespace Meziantou.Framework.SnapshotTesting;

internal static class ImageLimits
{
    // Decoded images are stored as one ARGB value per pixel in a single array. The limit keeps a
    // malformed header from requesting an allocation larger than any snapshot image would need.
    public const long MaxPixelCount = 256L * 1024 * 1024;

    public static bool IsValidSize(long width, long height)
    {
        return width > 0 && height > 0 && width * height <= MaxPixelCount;
    }
}
