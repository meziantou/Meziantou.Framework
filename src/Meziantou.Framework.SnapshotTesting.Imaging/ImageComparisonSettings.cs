namespace Meziantou.Framework.SnapshotTesting.Imaging;

/// <summary>
/// Configures the comparison behavior used by <see cref="ImageComparer"/>. The thresholds apply to every frame; they use
/// <see cref="Meziantou.Framework.Imaging.ImageSimilarity"/>.
/// </summary>
public sealed class ImageComparisonSettings
{
    private float? _similarityThreshold;
    private int? _dHashThreshold;
    private int? _pHashThreshold;
    private double? _psnrThreshold;
    private double? _ssimulacra2Threshold;
    private double? _butteraugliThreshold;

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
    /// out of 255), capped at 64. A bit is set only when the left thumbnail cell is brighter than the right one by more
    /// than half a luminance level, so invisible noise in flat areas does not flip bits. Images that are not fully opaque
    /// are compared composited over a black and over a white background, and the larger distance is used. Images must have
    /// the same dimensions.
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
    /// The distance is computed like the one used by <see cref="DHashThreshold"/>. A bit is set only when its DCT coefficient
    /// exceeds the median coefficient by more than one luminance level, so invisible noise in flat areas does not flip bits.
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

    /// <summary>
    /// Gets or sets the minimum peak signal-to-noise ratio (PSNR), in decibels, required for two images to be considered
    /// equal. Identical images have an infinite ratio; lossy encodings are typically between 30 and 50. When
    /// <see langword="null"/>, PSNR comparison is not performed.
    /// </summary>
    /// <remarks>
    /// The ratio is computed from the mean squared error of the R, G and B channels premultiplied by alpha, the value
    /// scikit-image's <c>peak_signal_noise_ratio(expected, actual, data_range=255)</c> computes for opaque images. The alpha
    /// channel gives its own ratio, and the lower of the two is used. Like SSIM, it is a mean: a localized difference lowers
    /// it less than the same difference spread over the image. Images must have the same dimensions.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not a number.</exception>
    public double? PsnrThreshold
    {
        get => _psnrThreshold;
        set
        {
            if (value is { } threshold && !(threshold >= 0))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The PSNR threshold must be a positive number of decibels.");

            _psnrThreshold = value;
        }
    }

    /// <summary>
    /// Gets or sets the minimum SSIMULACRA 2 score required for two images to be considered equal. Scores go up to
    /// <c>100</c> (identical); about <c>90</c> means the difference is not visible, <c>70</c> a high quality. When
    /// <see langword="null"/>, SSIMULACRA 2 comparison is not performed.
    /// </summary>
    /// <remarks>
    /// The score is the one of the <c>ssimulacra2</c> tool of libjxl, a perceptual metric that compares the images in the
    /// XYB color space at 6 scales and penalizes new artifacts (ringing, banding, blockiness) and lost details (blurring).
    /// Images that are not fully opaque are composited over a dark and a bright gray, and the lower score is used. The
    /// metric needs images of at least 8×8 pixels: smaller images pass this check only when their pixels are identical
    /// (fully transparent pixels are equal whatever color they hide). Images must have the same dimensions.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is greater than <c>100</c>, infinite or not a number.</exception>
    public double? Ssimulacra2Threshold
    {
        get => _ssimulacra2Threshold;
        set
        {
            if (value is { } threshold && !(double.IsFinite(threshold) && threshold <= 100))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The SSIMULACRA 2 threshold must be a finite number less than or equal to 100.");

            _ssimulacra2Threshold = value;
        }
    }

    /// <summary>
    /// Gets or sets the maximum Butteraugli distance for two images to be considered equal: <c>0</c> for identical
    /// images, below about <c>1</c> when the difference is not visible. When <see langword="null"/>, Butteraugli comparison
    /// is not performed.
    /// </summary>
    /// <remarks>
    /// The distance is the largest value of the Butteraugli map of libjxl (<see cref="Meziantou.Framework.Imaging.ButteraugliDistance.MaxDistance"/>),
    /// a psychovisual model that gives a distance for every pixel: unlike the SSIM and PSNR means, a difference confined to a
    /// small area weighs fully. Images that are not fully opaque are composited over black and over white, and every pixel
    /// keeps the larger distance. It is the slowest check. Images must have the same dimensions.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative, infinite or not a number.</exception>
    public double? ButteraugliThreshold
    {
        get => _butteraugliThreshold;
        set
        {
            if (value is { } threshold && !(double.IsFinite(threshold) && threshold >= 0))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The Butteraugli threshold must be a finite, positive number.");

            _butteraugliThreshold = value;
        }
    }

    internal bool HasSimilarityThreshold => SimilarityThreshold is not null || DHashThreshold is not null || PHashThreshold is not null
        || PsnrThreshold is not null || Ssimulacra2Threshold is not null || ButteraugliThreshold is not null;

    private static void ValidateHashThreshold(int? value, string parameterName)
    {
        if (value is < 0 or > 64)
            throw new ArgumentOutOfRangeException(parameterName, value, "The hash threshold must be between 0 and 64.");
    }
}
