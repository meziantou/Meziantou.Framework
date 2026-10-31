namespace Meziantou.Framework.Imaging;

/// <summary>
/// Immutable options of <see cref="ImageSimilarity.ComputeSsim(ImageFrame, ImageFrame, ImageSimilarityOptions, CancellationToken)"/>
/// and <see cref="ImageSimilarity.ComputePsnr(ImageFrame, ImageFrame, ImageSimilarityOptions, CancellationToken)"/>: the
/// precision of the samples they compare.
/// </summary>
public sealed class ImageSimilarityOptions
{
    /// <summary>Gets the samples that are compared. Defaults to <see cref="SimilarityPrecision.EightBit"/>, the samples of the overloads without options.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="SimilarityPrecision"/>.</exception>
    public SimilarityPrecision Precision
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The precision is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the range of the color samples with <see cref="SimilarityPrecision.FloatingPoint"/>: the peak of the PSNR and
    /// the <c>L</c> of the SSIM constants, scikit-image's <c>data_range</c>. Defaults to 1, the reference white; use the
    /// largest value of high dynamic range frames, such as 4 for a highlight headroom of 4. Alpha, a coverage, always has
    /// the range 1. The other precisions have the range of their samples (255 or 65,535).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive and finite.</exception>
    public double DataRange
    {
        get;
        init
        {
            if (!(value > 0 && double.IsFinite(value)))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The data range must be positive and finite.");

            field = value;
        }
    } = 1;
}
