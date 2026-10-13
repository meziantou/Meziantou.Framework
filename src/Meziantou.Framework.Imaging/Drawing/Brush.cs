namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>The colors a shape is painted with: a <see cref="SolidBrush"/> or a <see cref="GradientBrush"/>.</summary>
/// <remarks>
/// <para>
/// Brushes are immutable and thread-safe: create them once and reuse them. The type cannot be derived outside the library.
/// </para>
/// <para>
/// Brush colors are sample values in the color space of the image they are drawn on: no color profile is applied. Before
/// painting, a color is converted to the pixel format of the frame with the rules of <see cref="Image.CloneAs{TPixel}(PixelConversionOptions?)"/>
/// (Rec. 709 luma on grayscale frames, rounding to 8 bits on 8-bit frames); its alpha stays the opacity of the paint, also
/// on frames without alpha.
/// </para>
/// </remarks>
public abstract class Brush
{
    private protected Brush()
    {
    }

    /// <summary>Gets how the paint is combined with the pixels it is drawn on. Defaults to <see cref="Drawing.CompositeMode.SourceOver"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Drawing.CompositeMode"/>.</exception>
    public CompositeMode CompositeMode
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The composite mode is not valid.");

            field = value;
        }
    }

    /// <summary>Creates a brush painting a single color.</summary>
    /// <param name="color">The color, with straight alpha.</param>
    /// <returns>A new <see cref="SolidBrush"/>.</returns>
    public static Brush FromRgba64(Rgba64 color) => new SolidBrush(color);

    /// <summary>Creates a brush painting a single color.</summary>
    /// <param name="color">The color, with straight alpha.</param>
    /// <returns>A new <see cref="SolidBrush"/>.</returns>
    public static Brush FromRgba32(Rgba32 color) => new SolidBrush(color);

    /// <summary>Creates a brush painting a single color. A new <see cref="SolidBrush"/> is allocated by each conversion.</summary>
    /// <param name="color">The color, with straight alpha.</param>
    public static implicit operator Brush(Rgba64 color) => FromRgba64(color);

    /// <summary>Creates a brush painting a single color. A new <see cref="SolidBrush"/> is allocated by each conversion.</summary>
    /// <param name="color">The color, with straight alpha.</param>
    public static implicit operator Brush(Rgba32 color) => FromRgba32(color);
}
