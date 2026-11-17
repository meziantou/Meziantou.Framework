namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Immutable settings for Radiance HDR encoding (RGBE: linear-light, high dynamic range still images).</summary>
/// <remarks>
/// <para>
/// The output is an RGBE picture (<c>#?RADIANCE</c>, <c>FORMAT=32-bit_rle_rgbe</c>) in the standard orientation
/// (<c>-Y height +X width</c>): the scanlines are the rows, from the top. Each pixel stores an 8-bit mantissa per color and
/// an exponent shared by the three colors (Greg Ward, "Real Pixels", Graphics Gems II): the largest color keeps 8
/// significant bits, and the others keep what the shared exponent leaves them. This rounding is the format itself, like the
/// quantization of a lossy format: the decoder reads a mantissa <c>m</c> as the middle of its interval,
/// <c>(m + 0.5) × 2^(e - 136)</c>, so a color comes back within half a unit of the last place of the largest color of
/// its pixel. A pixel whose largest color is at most 1E-32 is written black.
/// </para>
/// <para>
/// Floating-point pixels may have any finite value from 0 to 2^127 (excluded). A negative color, an infinity or a sample that
/// is not a number cannot be stored, and is rejected with <see cref="UnsupportedImageFeatureException"/> before anything
/// is written (clamp the colors of the image first). Integer pixels are stored as the
/// floating-point values of the conversion of <see cref="Image.CloneAs{TPixel}(PixelConversionOptions?)"/> to
/// <see cref="RgbaFloat"/> (<c>v / max</c>); gray pixel formats are written with three equal colors.
/// <see cref="ImageEncoder.FloatingPointConversion"/> does not apply.
/// </para>
/// <para>
/// Radiance HDR has no alpha channel: a pixel that is not fully opaque (alpha other than 1) requires
/// <see cref="BackgroundColor"/>, over which it is composited, and is rejected otherwise.
/// </para>
/// <para>
/// The samples are linear light: the image must be labeled <see cref="Metadata.ColorTransferFunction.Linear"/> (the default
/// of floating-point images), or have a CICP description with the linear transfer characteristics (8) and the primaries of
/// BT.709, BT.2020, DCI-P3 or Display P3. The header records the primaries (<c>PRIMARIES</c>, those of BT.709 for an image
/// labeled linear). Any other label (sRGB-encoded samples, an ICC profile), EXIF, XMP, an orientation other than
/// <see cref="Metadata.ExifOrientation.TopLeft"/>, a resolution or text entries follow <see cref="ImageEncoder.MetadataHandling"/>
/// (rejected by default). <see cref="Metadata.ImageMetadata.RadianceHeader"/> is ignored.
/// </para>
/// <para>
/// Radiance HDR stores exactly one frame: animated images and images with a poster frame are rejected with an
/// <see cref="UnsupportedImageFeatureException"/>; use <see cref="ImageFrame.Save(string, ImageEncoder?)"/> to save one frame
/// explicitly.
/// </para>
/// </remarks>
public sealed class RadianceEncoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Radiance;

    /// <summary>Gets the encoding of the scanlines. Defaults to <see cref="RadianceCompression.RunLength"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="RadianceCompression"/>.</exception>
    public RadianceCompression Compression
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The compression is not valid.");

            field = value;
        }
    } = RadianceCompression.RunLength;

    /// <summary>
    /// Gets the opaque background color non-opaque pixels are composited over, as linear-light fractions of one
    /// (<c>v / 65535</c>), or <see langword="null"/> to reject non-opaque pixels. Fully opaque images never need it.
    /// </summary>
    /// <remarks>
    /// A pixel of alpha <c>a</c> (clamped to [0, 1]) becomes <c>a × color + (1 - a) × background</c>, computed in double
    /// precision; its color is then checked like any other.
    /// </remarks>
    /// <exception cref="ArgumentException">The color is not fully opaque.</exception>
    public Rgba64? BackgroundColor
    {
        get;
        init
        {
            if (value is { A: not ushort.MaxValue })
                throw new ArgumentException("The background color must be fully opaque.", nameof(value));

            field = value;
        }
    }

    /// <summary>Gets a value indicating whether floating-point samples are stored without reduction to integer pixels: always for Radiance HDR.</summary>
    internal override bool StoresFloatingPointSamples => true;
}
