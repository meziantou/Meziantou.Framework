namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// Configures the comparison behavior used by <see cref="ImageComparer"/>.
/// </summary>
public sealed class ImageComparisonSettings
{
    /// <summary>
    /// Gets or sets the minimum Structural Similarity Index (SSIM) score required for two BMP images to be considered equal.
    /// Values range from <c>0.0</c> (completely different) to <c>1.0</c> (identical). When <see langword="null"/>, exact pixel comparison is used.
    /// </summary>
    public float? SimilarityThreshold { get; set; }
}
