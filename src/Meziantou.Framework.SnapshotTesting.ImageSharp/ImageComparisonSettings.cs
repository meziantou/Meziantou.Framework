namespace Meziantou.Framework.SnapshotTesting.ImageSharp;

/// <summary>
/// Configures the image comparison behavior used by <see cref="ImageSharpSnapshotComparer"/>.
/// </summary>
public sealed class ImageComparisonSettings
{
    /// <summary>
    /// Gets or sets the minimum Structural Similarity Index (SSIM) score required for two images
    /// to be considered equal. Values range from <c>0.0</c> (completely different) to <c>1.0</c> (identical).
    /// When <see langword="null"/>, an exact pixel-by-pixel comparison is performed instead.
    /// </summary>
    public float? SimilarityThreshold { get; set; }
}
