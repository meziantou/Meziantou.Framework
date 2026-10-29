namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Immutable settings for Netpbm encoding (PBM, PGM, PPM and PAM).</summary>
/// <remarks>
/// <para>
/// With <see cref="PnmPixelLayout.Auto"/> the variant follows the pixel format: gray pixel formats are written as a
/// portable graymap (<c>P5</c>, or <c>P2</c> with <see cref="PnmEncoding.Plain"/>), pixel formats without alpha as a
/// portable pixmap (<c>P6</c>, or <c>P3</c>), and pixel formats with alpha as a portable arbitrary map (<c>P7</c>, tuple
/// type <c>RGB_ALPHA</c>). <c>MAXVAL</c> is 255 for 8-bit pixel formats and 65,535 for 16-bit ones, so the output is always
/// lossless.
/// </para>
/// <para>
/// A portable bitmap (<c>P4</c>, or <c>P1</c> with <see cref="PnmEncoding.Plain"/>) is only written on request, with
/// <see cref="PnmPixelLayout.Bitmap"/>. It is lossless too: every pixel must be exactly black or exactly white, and any
/// other pixel is rejected with an <see cref="UnsupportedImageFeatureException"/> before anything is written. A portable
/// bitmap is read back as <see cref="PixelFormat.Gray8"/> with one bit per component.
/// </para>
/// <para>
/// An encoder never looks at the file extension. Only the save and writer overloads that take a path and no encoder do:
/// they map <c>.pbm</c> to <see cref="PnmPixelLayout.Bitmap"/> and the other Netpbm extensions to the default settings.
/// </para>
/// <para>
/// Neither the plain forms nor a portable bitmap store alpha (PAM has no plain form): <see cref="PnmEncoding.Plain"/> or
/// <see cref="PnmPixelLayout.Bitmap"/> with a pixel format that has alpha discards alpha and therefore requires
/// <see cref="BackgroundColor"/>.
/// </para>
/// <para>
/// PNM stores exactly one frame: animated images and images with a poster frame are rejected with an
/// <see cref="UnsupportedImageFeatureException"/>; use <see cref="ImageFrame.Save(string, ImageEncoder?)"/> to save one frame explicitly.
/// Several images may be concatenated in one Netpbm file; this version never writes (nor reads) more than one.
/// </para>
/// <para>
/// Netpbm files store no metadata: an ICC profile, EXIF, XMP, an orientation other than
/// <see cref="Metadata.ExifOrientation.TopLeft"/>, a resolution and text entries follow
/// <see cref="ImageEncoder.MetadataHandling"/> (rejected by default).
/// </para>
/// </remarks>
public sealed class PnmEncoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Pnm;

    /// <summary>Gets the raster encoding. Defaults to <see cref="PnmEncoding.Binary"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="PnmEncoding"/>.</exception>
    public PnmEncoding Encoding
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The encoding is not valid.");

            field = value;
        }
    }

    /// <summary>Gets the pixel layout. Defaults to <see cref="PnmPixelLayout.Auto"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="PnmPixelLayout"/>.</exception>
    public PnmPixelLayout PixelLayout
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
    /// Gets the opaque background color non-opaque pixels are composited over when <see cref="PnmEncoding.Plain"/> or
    /// <see cref="PnmPixelLayout.Bitmap"/> discards alpha, interpreted in the encoded color space of the image (sRGB when
    /// untagged), or <see langword="null"/> to reject non-opaque pixels. Fully opaque images never need it, and a portable
    /// arbitrary map (<see cref="PnmEncoding.Binary"/> with <see cref="PnmPixelLayout.Auto"/>) ignores it because it stores
    /// alpha.
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
}
