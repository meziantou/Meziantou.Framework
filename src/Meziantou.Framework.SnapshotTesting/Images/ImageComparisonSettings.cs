namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// Configures the comparison behavior used by <see cref="ImageComparer"/>.
/// </summary>
public sealed class ImageComparisonSettings
{
    private float? _similarityThreshold;
    private int? _dHashThreshold;
    private int? _pHashThreshold;

    /// <summary>
    /// Gets or sets the minimum Structural Similarity Index (SSIM) score required for two images to be considered equal.
    /// Values range from <c>0.0</c> to <c>1.0</c> (identical). When all thresholds are <see langword="null"/>, exact pixel comparison is used.
    /// </summary>
    /// <remarks>
    /// The score is the mean SSIM over every 7×7 window of the images (uniform weights, sample covariance,
    /// <c>K1 = 0.01</c>, <c>K2 = 0.03</c>), the value scikit-image's <c>structural_similarity</c> computes with its default
    /// parameters. The R, G and B channels are premultiplied by the alpha channel and averaged, and the result is the lower
    /// of that score and the score of the alpha channel. Because the score is a mean, a localized difference lowers it in
    /// proportion to the area it covers: a difference confined to 1% of the image lowers it by about 0.01 at most.
    /// Images must have the same dimensions.
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

    /// <summary>
    /// Gets or sets the maximum distance between the 64-bit difference hashes (dHash) for two images to be considered equal.
    /// Values range from <c>0</c> (identical hashes) to <c>64</c>. When <see langword="null"/>, dHash comparison is not performed.
    /// </summary>
    /// <remarks>
    /// The distance is the Hamming distance between the hashes, plus the difference between the mean luminances of the
    /// images on the same scale (a solid black and a solid white image are 64 apart; each unit is about 4 luminance levels
    /// out of 255), capped at 64. Images that are not fully opaque are compared composited over a black and over a white
    /// background, and the larger distance is used. Images must have the same dimensions.
    /// </remarks>
    public int? DHashThreshold
    {
        get => _dHashThreshold;
        set
        {
            ValidateHashThreshold(value, nameof(DHashThreshold));
            _dHashThreshold = value;
        }
    }

    /// <summary>
    /// Gets or sets the maximum distance between the 64-bit perceptual hashes (pHash) for two images to be considered equal.
    /// Values range from <c>0</c> (identical hashes) to <c>64</c>. When <see langword="null"/>, pHash comparison is not performed.
    /// </summary>
    /// <remarks>
    /// The distance is computed like the one used by <see cref="DHashThreshold"/>.
    /// </remarks>
    public int? PHashThreshold
    {
        get => _pHashThreshold;
        set
        {
            ValidateHashThreshold(value, nameof(PHashThreshold));
            _pHashThreshold = value;
        }
    }

    private static void ValidateHashThreshold(int? value, string parameterName)
    {
        if (value is < 0 or > 64)
            throw new ArgumentOutOfRangeException(parameterName, value, "The hash threshold must be between 0 and 64.");
    }
}
