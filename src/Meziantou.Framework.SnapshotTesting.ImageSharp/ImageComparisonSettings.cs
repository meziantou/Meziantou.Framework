namespace Meziantou.Framework.SnapshotTesting.ImageSharp;

/// <summary>
/// Configures the image comparison behavior used by <see cref="ImageSharpSnapshotComparer"/>.
/// </summary>
public sealed class ImageComparisonSettings
{
    private float? _similarityThreshold;

    /// <summary>
    /// Gets or sets the minimum Structural Similarity Index (SSIM) score required for two images
    /// to be considered equal. Values range from <c>0.0</c> to <c>1.0</c> (identical).
    /// When <see langword="null"/>, an exact pixel-by-pixel comparison is performed instead.
    /// </summary>
    /// <remarks>
    /// The score is the mean SSIM over every 7×7 window of the images (uniform weights, sample covariance,
    /// <c>K1 = 0.01</c>, <c>K2 = 0.03</c>), the value scikit-image's <c>structural_similarity</c> computes with its default
    /// parameters. The R, G and B channels are premultiplied by the alpha channel and averaged, and the result is the lower
    /// of that score and the score of the alpha channel. Because the score is a mean, a localized difference lowers it in
    /// proportion to the area it covers: a difference confined to 1% of the image lowers it by about 0.01 at most.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between <c>0.0</c> and <c>1.0</c>.</exception>
    public float? SimilarityThreshold
    {
        get => _similarityThreshold;
        set
        {
            if (value is not (null or (>= 0 and <= 1)))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The similarity threshold must be between 0 and 1.");

            _similarityThreshold = value;
        }
    }
}
