namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Immutable settings for BMP encoding (uncompressed Windows bitmaps).</summary>
/// <remarks>
/// <para>
/// The output is the interoperable uncompressed subset: bottom-up rows padded to four bytes, either 24-bit <c>BI_RGB</c> in a
/// <c>BITMAPINFOHEADER</c> or 32-bit <c>BI_BITFIELDS</c> in a <c>BITMAPV4HEADER</c> with an explicit alpha mask
/// (<see cref="PixelLayout"/>). Run-length encoding, embedded JPEG/PNG payloads, indexed output, OS/2 headers and top-down
/// rows are never written.
/// </para>
/// <para>
/// Gray pixel formats are written as gray RGB (R = G = B), and 16-bit pixel formats require
/// <see cref="AllowBitDepthReduction"/>. Alpha is preserved by <see cref="BmpPixelLayout.Bgra32"/>; discarding it with
/// <see cref="BmpPixelLayout.Bgr24"/> requires <see cref="BackgroundColor"/>.
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
    /// Gets the opaque background color non-opaque pixels are composited over when <see cref="BmpPixelLayout.Bgr24"/>
    /// discards alpha, interpreted in the encoded color space of the image (sRGB when untagged), or <see langword="null"/> to
    /// reject non-opaque pixels. Fully opaque images never need it, and <see cref="BmpPixelLayout.Bgra32"/> ignores it.
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

    /// <summary>Gets a value indicating whether 16-bit pixel formats may be reduced to 8 bits per component (nearest rounding). Defaults to <see langword="false"/>.</summary>
    public bool AllowBitDepthReduction { get; init; }
}
