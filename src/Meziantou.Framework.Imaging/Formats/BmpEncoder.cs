namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Immutable settings for BMP encoding (Windows bitmaps).</summary>
/// <remarks>
/// <para>
/// By default the output is the most interoperable subset: uncompressed bottom-up rows padded to four bytes, either 24-bit
/// <c>BI_RGB</c> in a <c>BITMAPINFOHEADER</c> or 32-bit <c>BI_BITFIELDS</c> in a <c>BITMAPV4HEADER</c> with an explicit
/// alpha mask (<see cref="BmpPixelLayout.Auto"/>). <see cref="PixelLayout"/> also selects 1-, 4- and 8-bit indexed layouts
/// and 16-bit 5-6-5 and 5-5-5 layouts, <see cref="Compression"/> run-length encodes the 4- and 8-bit indexed layouts, and
/// <see cref="RowOrder"/> writes top-down rows. Embedded JPEG/PNG payloads and OS/2 headers are never written.
/// </para>
/// <para>
/// Nothing is lost silently. Gray pixel formats are written as gray RGB (R = G = B). Alpha is only preserved by
/// <see cref="BmpPixelLayout.Bgra32"/>: every other layout requires <see cref="BackgroundColor"/> as soon as a pixel is not
/// opaque. 16-bit pixel formats, and the 16-bit layouts, require <see cref="AllowBitDepthReduction"/>. An indexed layout
/// stores the colors of the image exactly when its palette can hold them, and requires <see cref="AllowColorReduction"/>
/// otherwise (see <see cref="Quantization"/>).
/// </para>
/// <para>
/// The indexed layouts read the frame twice (its colors, then its pixels) and keep one byte per pixel of working memory,
/// charged to the resource limits; the other layouts keep one row.
/// </para>
/// <para>
/// BMP stores exactly one frame: animated images and images with a poster frame are rejected with an
/// <see cref="UnsupportedImageFeatureException"/>; use <see cref="Image.CloneFrame(int)"/> to export one frame explicitly.
/// </para>
/// <para>
/// Metadata (subject to <see cref="ImageEncoder.MetadataHandling"/>): only the resolution
/// (<c>biXPelsPerMeter</c>/<c>biYPelsPerMeter</c>). An ICC profile, EXIF, XMP, an orientation other than
/// <see cref="Metadata.ExifOrientation.TopLeft"/> and text entries cannot be stored (rejected by default).
/// </para>
/// </remarks>
public sealed class BmpEncoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Bmp;

    /// <summary>Gets the pixel layout. Defaults to <see cref="BmpPixelLayout.Auto"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="BmpPixelLayout"/>.</exception>
    public BmpPixelLayout PixelLayout
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The pixel layout is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the compression. Defaults to <see cref="BmpCompression.None"/>. <see cref="BmpCompression.RunLength"/> requires
    /// <see cref="BmpPixelLayout.Indexed4"/> or <see cref="BmpPixelLayout.Indexed8"/> and <see cref="BmpRowOrder.BottomUp"/>;
    /// saving fails with an <see cref="ArgumentException"/> otherwise.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="BmpCompression"/>.</exception>
    public BmpCompression Compression
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The compression is not valid.");

            field = value;
        }
    }

    /// <summary>Gets the order of the rows. Defaults to <see cref="BmpRowOrder.BottomUp"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="BmpRowOrder"/>.</exception>
    public BmpRowOrder RowOrder
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The row order is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the opaque background color non-opaque pixels are composited over by the layouts that discard alpha (every
    /// layout but <see cref="BmpPixelLayout.Bgra32"/>), interpreted in the encoded color space of the image (sRGB when
    /// untagged), or <see langword="null"/> to reject non-opaque pixels. Fully opaque images never need it, and
    /// <see cref="BmpPixelLayout.Bgra32"/> ignores it.
    /// </summary>
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

    /// <summary>
    /// Gets a value indicating whether samples may be reduced to the precision of the layout (nearest rounding): 16-bit
    /// pixel formats to 8 bits per component, and any pixel format to the 5 or 6 bits per component of
    /// <see cref="BmpPixelLayout.Bgr565"/> and <see cref="BmpPixelLayout.Bgr555"/>. Defaults to <see langword="false"/>.
    /// </summary>
    public bool AllowBitDepthReduction { get; init; }

    /// <summary>
    /// Gets a value indicating whether an indexed layout may reduce the colors of an image whose colors its palette cannot
    /// hold exactly (more distinct colors than the smaller of <see cref="QuantizeOptions.MaxColors"/> and the capacity of the
    /// layout, or colors that are not in <see cref="QuantizeOptions.Palette"/>). Defaults to <see langword="false"/>: such an
    /// image is rejected with an <see cref="UnsupportedImageFeatureException"/>. When <see langword="true"/>, the colors are
    /// reduced as <see cref="Quantization"/> says. An image whose colors fit is always stored exactly. Ignored by the other
    /// layouts.
    /// </summary>
    public bool AllowColorReduction { get; init; }

    /// <summary>
    /// Gets how an indexed layout reduces colors to its palette. Defaults to <see cref="QuantizeOptions.Default"/>: at most
    /// 256 entries, further limited to the capacity of the layout (2, 16 or 256), without dithering. Ignored by the other
    /// layouts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The palette is the exact colors of the image when they fit, otherwise a deterministic variance-based median cut of its
    /// colors (see <see cref="PaletteBuilder"/>), with the dithering of <see cref="QuantizeOptions.Dithering"/>; the colors are
    /// only reduced with <see cref="AllowColorReduction"/>. The palette only stores the entries it has (<c>biClrUsed</c>).
    /// </para>
    /// <para>
    /// With <see cref="QuantizeOptions.Palette"/>, the pixels map to that palette, whose entries that are not opaque are
    /// composited over <see cref="BackgroundColor"/> first. Saving fails with an <see cref="ArgumentException"/> when an entry
    /// is not opaque without <see cref="BackgroundColor"/>, or when the palette has more colors than the layout holds.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public QuantizeOptions Quantization
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = QuantizeOptions.Default;

    /// <summary>
    /// Gets the maximum number of palette entries of an indexed layout: <see cref="Quantization"/>.<see cref="QuantizeOptions.MaxColors"/>,
    /// which it sets.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 2 and 256.</exception>
    [Obsolete("Use Quantization.MaxColors.")]
    public int MaxColors
    {
        get => Quantization.MaxColors;
        init => Quantization = Quantization.With(maxColors: value);
    }

    /// <summary>
    /// Gets the dithering applied when an indexed layout reduces colors: <see cref="Quantization"/>.<see cref="QuantizeOptions.Dithering"/>,
    /// which it sets.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="BmpDithering"/>.</exception>
    [Obsolete("Use Quantization.Dithering, which also selects other dithering modes.")]
    public BmpDithering Dithering
    {
        get => (BmpDithering)Quantization.Dithering;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The dithering mode is not valid.");

            Quantization = Quantization.With(dithering: (DitheringMode)value);
        }
    }
}
