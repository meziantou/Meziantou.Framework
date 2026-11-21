namespace Meziantou.Framework.Imaging;

/// <summary>Immutable options of <see cref="PixelMask.FromSimilarColors(ImageFrame, Point, ColorSelectionOptions?, CancellationToken)"/>.</summary>
public sealed class ColorSelectionOptions
{
    /// <summary>Gets the default options: the exact color of the seed, and the pixels connected to it by their sides.</summary>
    public static ColorSelectionOptions Default { get; } = new();

    /// <summary>
    /// Gets how far, as a fraction of the range of a sample, each sample of a pixel can be from the one of the seed, from
    /// 0 to 1. Defaults to 0: the exact color of the seed. The comparison is the one of
    /// <see cref="ImageProcessingExtensions.ReplaceColor(Image, Rgba64, Rgba64, double, CancellationToken)"/>: the four
    /// samples of the 16-bit color, alpha included, each within the tolerance times 65535, rounded to nearest with ties
    /// upward; <c>1 / 255</c> is the next 8-bit value on each side.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1.</exception>
    public double Tolerance
    {
        get;
        init
        {
            if (!(value >= 0 && value <= 1))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The tolerance must be between 0 and 1.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the neighbors a selection grows to when it is <see cref="Contiguous"/>. Defaults to
    /// <see cref="PixelConnectivity.Four"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a member of <see cref="PixelConnectivity"/>.</exception>
    public PixelConnectivity Connectivity
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The connectivity is not valid.");

            field = value;
        }
    } = PixelConnectivity.Four;

    /// <summary>
    /// Gets whether the selection is made of the pixels connected to the seed only, through pixels of a similar color
    /// (the magic wand of image editors), or of every pixel of a similar color in the frame. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool Contiguous { get; init; } = true;
}
