using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging;

/// <summary>Immutable options of <see cref="ImageProcessingExtensions.AddWatermark(Image, string, ImageWatermarkOptions, CancellationToken)"/>.</summary>
/// <remarks>
/// The library never picks a font by itself: the fonts are given to the constructor, so the watermark looks the same on
/// every machine. The defaults draw the text in gray at 30% opacity, along the diagonal of the image, in its center.
/// </remarks>
public sealed class ImageWatermarkOptions
{
    /// <summary>The largest font size, in pixels.</summary>
    public const double MaxFontSize = 65536;

    /// <summary>The largest margin and tile spacing, in pixels.</summary>
    public const double MaxLength = 16777216;

    /// <summary>Initializes a new instance of the <see cref="ImageWatermarkOptions"/> class.</summary>
    /// <param name="fonts">The fonts of the text. A <see cref="Font"/> converts implicitly.</param>
    /// <exception cref="ArgumentNullException"><paramref name="fonts"/> is <see langword="null"/>.</exception>
    public ImageWatermarkOptions(FontStack fonts)
    {
        ArgumentNullException.ThrowIfNull(fonts);
        Fonts = fonts;
    }

    /// <summary>Gets the fonts of the text.</summary>
    public FontStack Fonts { get; }

    /// <summary>
    /// Gets the font size, in pixels, from more than 0 to <see cref="MaxFontSize"/>, or <see langword="null"/> (the
    /// default) for a size computed from the image: a single centered watermark fits the box of its rotated text in 80%
    /// of the image, and a watermark in a corner, against an edge or tiled uses a twentieth of the diagonal of the image.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive number up to <see cref="MaxFontSize"/>.</exception>
    public double? FontSize
    {
        get;
        init
        {
            if (value is { } size && !(size > 0 && size <= MaxFontSize))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The font size must be a positive number up to 65536.");

            field = value;
        }
    }

    /// <summary>Gets the color of the text, with straight alpha. The default is gray (128, 128, 128). An <see cref="Rgba32"/> converts implicitly.</summary>
    public Rgba64 Color { get; init; } = new Rgba32(128, 128, 128);

    /// <summary>Gets the opacity of the text, from 0 (invisible) to 1 (opaque). The default is 0.3.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1.</exception>
    public double Opacity
    {
        get;
        init
        {
            if (!(value >= 0 && value <= 1))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The opacity must be between 0 and 1.");

            field = value;
        }
    } = 0.3;

    /// <summary>
    /// Gets the angle of the text, in degrees, counterclockwise on the image, or <see langword="null"/> (the default) to
    /// follow the diagonal from the bottom-left corner to the top-right corner when the watermark is centered or tiled,
    /// and to keep the text horizontal when it is in a corner or against an edge.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a finite number.</exception>
    public double? Rotation
    {
        get;
        init
        {
            if (value is { } angle && !double.IsFinite(angle))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The rotation must be a finite number.");

            field = value;
        }
    }

    /// <summary>Gets where a single watermark is placed. The default is <see cref="WatermarkPosition.Center"/>. It is ignored when <see cref="IsTiled"/> is <see langword="true"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="WatermarkPosition"/>.</exception>
    public WatermarkPosition Position
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The position is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the distance, in pixels, between the box of the rotated text and the edges of the image it is placed against,
    /// or <see langword="null"/> (the default) for a fortieth of the smaller side of the image. It is ignored when the
    /// watermark is centered or tiled.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative, is not a finite number or exceeds <see cref="MaxLength"/>.</exception>
    public double? Margin
    {
        get;
        init
        {
            ValidateLength(value);
            field = value;
        }
    }

    /// <summary>Gets a value indicating whether the text is repeated across the whole image, in rows that are offset by half a copy, instead of drawn once. The default is <see langword="false"/>.</summary>
    public bool IsTiled { get; init; }

    /// <summary>
    /// Gets the gap, in pixels, between two copies of a tiled watermark, along the text and across it, or
    /// <see langword="null"/> (the default) for the height of the text.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative, is not a finite number or exceeds <see cref="MaxLength"/>.</exception>
    public double? TileSpacing
    {
        get;
        init
        {
            ValidateLength(value);
            field = value;
        }
    }

    private static void ValidateLength(double? value)
    {
        if (value is { } length && !(length >= 0 && length <= MaxLength))
            throw new ArgumentOutOfRangeException(nameof(value), value, "The length must be a number from 0 to 16777216.");
    }
}
