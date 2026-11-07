namespace Meziantou.Framework.Imaging;

/// <summary>Immutable options of <see cref="ImageSimilarity.CreateDifferenceMap(ImageFrame, ImageFrame, DifferenceMapOptions?, CancellationToken)"/>.</summary>
public sealed class DifferenceMapOptions
{
    /// <summary>Gets what the map shows. Defaults to <see cref="DifferenceMapKind.Highlight"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="DifferenceMapKind"/>.</exception>
    public DifferenceMapKind Kind
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The kind of difference map is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the largest difference of the samples of a pixel, as a fraction of their range, that
    /// <see cref="DifferenceMapKind.Highlight"/> does not highlight. Defaults to 0: every difference is highlighted, as
    /// ImageMagick's <c>compare</c> does without <c>-fuzz</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double Threshold
    {
        get;
        init
        {
            if (!(value >= 0 && double.IsFinite(value)))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The threshold must be a finite number that is not negative.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the color of the pixels that differ with <see cref="DifferenceMapKind.Highlight"/>, composited over the expected
    /// frame. Defaults to ImageMagick's <c>#f1001ecc</c>: red with an alpha of 0.8.
    /// </summary>
    public Rgba32 HighlightColor { get; init; } = new(0xF1, 0x00, 0x1E, 0xCC);

    /// <summary>
    /// Gets the color of the pixels that do not differ with <see cref="DifferenceMapKind.Highlight"/>, composited over the
    /// expected frame. Defaults to ImageMagick's <c>#ffffffcc</c>: white with an alpha of 0.8, which fades the frame.
    /// </summary>
    public Rgba32 LowlightColor { get; init; } = new(0xFF, 0xFF, 0xFF, 0xCC);

    /// <summary>
    /// Gets the factor applied to the differences of <see cref="DifferenceMapKind.Absolute"/> and
    /// <see cref="DifferenceMapKind.Ssim"/> before they are clamped to white. Defaults to 1; a gain of 10 shows a difference of
    /// 10% as white.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive and finite.</exception>
    public double Gain
    {
        get;
        init
        {
            if (!(value > 0 && double.IsFinite(value)))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The gain must be positive and finite.");

            field = value;
        }
    } = 1;
}
