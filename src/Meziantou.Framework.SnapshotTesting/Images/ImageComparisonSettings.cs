namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// Configures the comparison behavior used by <see cref="ImageComparer"/>.
/// </summary>
public sealed class ImageComparisonSettings
{
    private int? _dHashThreshold;
    private int? _pHashThreshold;

    /// <summary>
    /// Gets or sets the minimum Structural Similarity Index (SSIM) score required for two images to be considered equal.
    /// Values range from <c>0.0</c> (completely different) to <c>1.0</c> (identical). When all thresholds are <see langword="null"/>, exact pixel comparison is used.
    /// </summary>
    public float? SimilarityThreshold { get; set; }

    /// <summary>
    /// Gets or sets the maximum Hamming distance between the 64-bit difference hashes (dHash) for two images to be considered equal.
    /// Values range from <c>0</c> (identical hashes) to <c>64</c>. When <see langword="null"/>, dHash comparison is not performed.
    /// </summary>
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
    /// Gets or sets the maximum Hamming distance between the 64-bit perceptual hashes (pHash) for two images to be considered equal.
    /// Values range from <c>0</c> (identical hashes) to <c>64</c>. When <see langword="null"/>, pHash comparison is not performed.
    /// </summary>
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
