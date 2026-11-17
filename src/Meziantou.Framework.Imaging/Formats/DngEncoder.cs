namespace Meziantou.Framework.Imaging.Formats;

/// <summary>
/// Immutable settings for DNG encoding (Adobe Digital Negative specification 1.7.1): the image as linear raw data
/// (<c>LinearRaw</c>), with a camera profile whose camera color space is linear BT.709 RGB.
/// </summary>
/// <remarks>
/// <para>
/// The raw image is the first image file directory, without preview. It stores the samples of the image as they are:
/// 8 or 16-bit integers for integer pixel formats, 32-bit floating-point numbers for <see cref="RgbaFloat"/> and
/// <see cref="RgbaHalf"/> pixels. Gray pixel formats are written as a monochrome image (one sample per pixel), the others
/// as three samples per pixel; a DNG raw image has no transparency, so a pixel that is not fully opaque is rejected with an
/// <see cref="UnsupportedImageFeatureException"/> (flatten the image first). CMYK pixels are not stored.
/// </para>
/// <para>
/// The samples of an image labeled <see cref="Metadata.ColorTransferFunction.Linear"/> are the linear reference values
/// themselves (the white level is the largest sample). The samples of an sRGB image are mapped to linear values by a
/// linearization table (<c>LinearizationTable</c>) of the sRGB transfer function, whose 16-bit output is the precision
/// DNG gives the linear values: the stored samples are exact (<see cref="DngRawImage.CopyStoredRow"/>), and a reader
/// renders them from the table. Floating-point samples are linear light: such an image must be labeled linear (the
/// default of floating-point images), and its samples must be finite and not negative (clamp the colors first). Samples
/// above 1 keep their values: the white level is then the smallest integer at least as large as the largest sample, and the
/// baseline exposure (<c>BaselineExposure</c>, its base-2 logarithm) brings it back to 1.
/// </para>
/// <para>
/// The camera profile says that the camera color space is linear RGB with the primaries of BT.709 and the D65 white point
/// (<c>ColorMatrix1</c> and <c>ForwardMatrix1</c> of that space for the D65 illuminant, and a neutral white balance), so that
/// a DNG reader renders the colors of the image. <see cref="Image.Load(string, ImageDecodeOptions?)"/> renders them back to
/// <see cref="RgbaFloat"/> pixels in linear light, within the precision of the rational numbers of the matrices.
/// </para>
/// <para>
/// The orientation is written as the orientation of the raw image, and an XMP packet as its <c>XMP</c> tag. An ICC profile,
/// a CICP description other than one equivalent to sRGB, EXIF, a resolution and text entries follow
/// <see cref="ImageEncoder.MetadataHandling"/> (rejected by default). The file is a TIFF file whose image file directory is
/// written after the image data, so the destination must be seekable.
/// </para>
/// <para>
/// A DNG file stores one image: animated images and images with a poster frame are rejected with an
/// <see cref="UnsupportedImageFeatureException"/>; use <see cref="ImageFrame.Save(string, ImageEncoder?)"/> to save one frame
/// explicitly. <see cref="Metadata.ImageMetadata.DngProperties"/> is ignored.
/// </para>
/// </remarks>
public sealed class DngEncoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Dng;

    /// <summary>
    /// Gets the compression of the raw image, or <see langword="null"/> (the default) for lossless JPEG with integer samples and
    /// Deflate with floating-point samples.
    /// </summary>
    /// <remarks>
    /// Lossless JPEG stores integer samples and Deflate floating-point ones: another combination is rejected with an
    /// <see cref="UnsupportedImageFeatureException"/> before anything is written.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="DngCompression"/>.</exception>
    public DngCompression? Compression
    {
        get;
        init
        {
            if (value is { } compression && !Enum.IsDefined(compression))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The compression is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the width and height of the tiles of the raw image, a multiple of 16, or <see langword="null"/> (the default) for
    /// tiles of 256 pixels with lossless JPEG and strips of about 64 KiB otherwise.
    /// </summary>
    /// <remarks>
    /// A tile is never larger than the image: the tiles of an image narrower or shorter than this size have its width or
    /// height, as the DNG SDK writes them.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive multiple of 16, or is larger than 65,536.</exception>
    public int? TileSize
    {
        get;
        init
        {
            if (value is { } size && (size <= 0 || size % 16 != 0 || size > 65536))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The tile size is a positive multiple of 16, at most 65,536.");

            field = value;
        }
    }

    /// <summary>Gets a value indicating whether floating-point samples are stored without reduction to integer pixels: always, as 32-bit samples.</summary>
    internal override bool StoresFloatingPointSamples => true;

    /// <summary>Gray with alpha and 16-bit RGB pixels are written as monochrome and RGB samples, not widened to RGBA.</summary>
    internal override bool StoresPixelFormat(PixelFormat format) => format is PixelFormat.La16 or PixelFormat.La32 or PixelFormat.Rgb48 || base.StoresPixelFormat(format);
}
