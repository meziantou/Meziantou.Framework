namespace Meziantou.Framework.Imaging.Formats;

/// <summary>
/// Immutable JPEG 2000 encoder settings (ISO/IEC 15444-1). By default the output is a JP2 file compressed without loss:
/// every pixel format is stored as it is, 16-bit samples and the color of fully transparent pixels included.
/// </summary>
/// <remarks>
/// <para>
/// Gray pixel formats are written as one component, the other formats as red, green and blue components with the
/// multiple component transformation, and an opacity component when the pixel format has an alpha channel. The layout
/// follows the pixel format, never the pixels. The image is one tile with one quality layer, at most five decomposition
/// levels and code-blocks of 64 by 64 coefficients, coded with the block coder of ISO/IEC 15444-1: the high-throughput
/// block coder (HTJ2K) is read but never written.
/// </para>
/// <para>
/// A JP2 file stores the ICC profile, the resolution, the EXIF data (which carries the orientation) and the XMP packet.
/// The ICC profile must be a grayscale profile or a matrix-based RGB profile, the only ones a JP2 file allows; any other
/// profile follows <see cref="ImageEncoder.MetadataHandling"/>. A codestream stores no metadata at all.
/// </para>
/// <para>
/// With <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> above 1 (the configuration of the saved image), an image
/// of at least 65,536 pixels is transformed and its code-blocks are coded by up to that many workers; the bytes are those
/// of one worker.
/// </para>
/// </remarks>
public sealed class Jpeg2000Encoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Jpeg2000;

    /// <summary>Gets whether the image is compressed without loss. Defaults to <see cref="Jpeg2000Compression.Lossless"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Jpeg2000Compression"/>.</exception>
    public Jpeg2000Compression Compression
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The compression is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the quality of a <see cref="Jpeg2000Compression.Lossy"/> output, from 1 (smallest output) to 100 (best quality).
    /// Defaults to 90. It is ignored by a lossless output.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The quality sets the quantization step sizes of the sub-bands; there is no rate control, so the size of the output
    /// follows from the image. With the scale of <see cref="JpegEncoder.Quality"/>, <c>s = 5000 / Quality</c> below 50 and
    /// <c>s = 200 - 2 * Quality</c> otherwise (at least 1), the error that the quantization of a sub-band of the luminance
    /// (or of the gray levels) adds to the image has a step of <c>0.47 * s^(3/4)</c> levels of an 8-bit sample,
    /// proportionally more for deeper samples; the two chrominances are quantized twice as coarsely.
    /// </para>
    /// <para>
    /// The values are calibrated on <see cref="JpegEncoder"/>: on photographs, a quality gives the structural similarity
    /// (SSIM) of a JPEG file of the same quality, with chrominances that are not worse than its sub-sampled ones, in a file
    /// of about two thirds of the size (half of it at the quality 20). 100 is the finest quantization, still lossy.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and 100.</exception>
    public int Quality
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100);
            field = value;
        }
    } = 90;

    /// <summary>Gets whether the output is a JP2 file or a codestream alone. Defaults to <see cref="Jpeg2000Container.Jp2"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="Jpeg2000Container"/>.</exception>
    public Jpeg2000Container Container
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The container is not valid.");

            field = value;
        }
    }
}
