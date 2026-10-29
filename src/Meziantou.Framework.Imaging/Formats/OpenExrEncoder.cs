namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Immutable settings for OpenEXR encoding (lossless, linear-light floating-point still images).</summary>
/// <remarks>
/// <para>
/// The output is a single-part scan-line file whose data window and display window are the image. Floating-point pixels
/// may have values outside [0, 1], infinities and samples that are not numbers
/// (<see cref="ImageEncoder.FloatingPointConversion"/> does not apply). Integer pixels are stored as the floating-point
/// values of the conversion of <see cref="Image.CloneAs{TPixel}(PixelConversionOptions?)"/> to <see cref="RgbaFloat"/>
/// (<c>v / max</c>). Gray pixel formats are written as a luminance channel (<c>Y</c>), the formats with alpha with an
/// <c>A</c> channel.
/// </para>
/// <para>
/// Colors are multiplied by nonzero alpha before writing and divided by it when decoded, as OpenEXR stores premultiplied
/// colors and the library holds straight colors. Rounding, underflow or overflow can change them: lossless compression
/// preserves the stored samples, not necessarily the source pixels. A zero alpha keeps its colors without multiplication.
/// </para>
/// <para>
/// By default, the samples are written as <see cref="OpenExrPixelType.Half"/> for
/// <see cref="RgbaHalf"/> and 8-bit pixel formats, and as <see cref="OpenExrPixelType.Float"/> otherwise. Writing
/// <see cref="RgbaFloat"/> or 16-bit pixels as half samples rounds them (nearest, ties to even) and requires
/// <see cref="AllowBitDepthReduction"/>; a finite sample beyond ±65,504 (the largest half) is rejected.
/// </para>
/// <para>
/// OpenEXR samples are linear light: the image must be labeled <see cref="Metadata.ColorTransferFunction.Linear"/> (the
/// default of floating-point images), or have a CICP description with the linear transfer characteristics (8) and the
/// primaries of BT.709, BT.2020, DCI-P3 or Display P3, which are written as the <c>chromaticities</c> attribute. Any other
/// label (sRGB-encoded samples, an ICC profile), EXIF, XMP, an orientation other than
/// <see cref="Metadata.ExifOrientation.TopLeft"/>, a resolution or text entries follow <see cref="ImageEncoder.MetadataHandling"/>
/// (rejected by default).
/// </para>
/// <para>
/// Compressed output needs a seekable destination: the table of the offsets of the chunks precedes them.
/// </para>
/// </remarks>
public sealed class OpenExrEncoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.OpenExr;

    /// <summary>Gets the compression of the chunks. Defaults to <see cref="OpenExrCompression.Zip"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="OpenExrCompression"/>.</exception>
    public OpenExrCompression Compression
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The compression is not valid.");

            field = value;
        }
    } = OpenExrCompression.Zip;

    /// <summary>
    /// Gets the type of the samples, or <see langword="null"/> (the default) for half samples of <see cref="RgbaHalf"/>
    /// and 8-bit pixels, float samples otherwise. Alpha conversion can round colors independently of this choice.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="OpenExrPixelType"/>.</exception>
    public OpenExrPixelType? PixelType
    {
        get;
        init
        {
            if (value is { } type && !Enum.IsDefined(type))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The pixel type is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="RgbaFloat"/> and 16-bit pixels may be written as half samples
    /// (<see cref="PixelType"/> set to <see cref="OpenExrPixelType.Half"/>), which rounds them. Defaults to <see langword="false"/>.
    /// </summary>
    public bool AllowBitDepthReduction { get; init; }

    /// <summary>Gets a value indicating whether floating-point samples are stored without reduction to integer pixels: always for OpenEXR.</summary>
    internal override bool StoresFloatingPointSamples => true;
}
