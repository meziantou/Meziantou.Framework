namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Immutable settings for 8-bit JPEG encoding.</summary>
/// <remarks>
/// <para>
/// The output is a Huffman-coded JPEG (JFIF) whose quantization tables are those of ITU-T T.81 Annex K scaled by
/// <see cref="Quality"/>: by default a baseline sequential one (one interleaved scan, the typical Huffman tables of
/// Annex K), or a progressive one (<see cref="Process"/>). Gray pixel formats produce a single
/// grayscale component; color pixel formats produce full-range YCbCr subsampled according to <see cref="ChromaSubsampling"/>.
/// The component layout follows the pixel format, never the pixel values.
/// </para>
/// <para>
/// JPEG stores exactly one frame: animated images and images with a poster frame are rejected with an
/// <see cref="UnsupportedImageFeatureException"/>; use <see cref="ImageFrame.Save(string, ImageEncoder?)"/> to save one frame explicitly.
/// </para>
/// <para>
/// Losses beyond the inherent lossy compression require explicit settings: non-opaque pixels require
/// <see cref="BackgroundColor"/>, and 16-bit pixel formats require <see cref="AllowBitDepthReduction"/>. Both are checked
/// before any output is written.
/// </para>
/// <para>
/// Metadata (subject to <see cref="ImageEncoder.MetadataHandling"/>): EXIF (APP1, orientation and dimensions reconciled), standard
/// XMP (APP1), ICC profile (APP2, split into chunks), resolution (JFIF density) and <c>Comment</c> text entries (COM, Latin-1).
/// </para>
/// <para>
/// The MCU rows of a frame of at least 65,536 pixels are converted, transformed and quantized by up to
/// <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> workers of the configuration of the image, a batch of rows at a
/// time; the entropy coding stays sequential, so the output does not depend on the number of workers.
/// </para>
/// </remarks>
public sealed class JpegEncoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Jpeg;

    /// <summary>Gets the quality, from 1 (smallest) to 100 (best). Defaults to 90.</summary>
    /// <remarks>
    /// The Annex K tables are scaled linearly: with <c>s = 5000 / Quality</c> below 50 and <c>s = 200 - 2 * Quality</c>
    /// otherwise, each quantization value is <c>clamp((base * s + 50) / 100, 1, 255)</c>. Quality 50 uses the Annex K
    /// tables unchanged and 100 quantizes every coefficient by 1 (still lossy: color conversion and rounding remain).
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

    /// <summary>Gets the chroma subsampling. Defaults to <see cref="JpegChromaSubsampling.Auto"/>.</summary>
    /// <remarks>
    /// Subsampled chroma is the mean of the full-resolution samples each chroma sample covers (box filter, centered siting).
    /// The setting has no effect on gray pixel formats, which are written as a single component.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="JpegChromaSubsampling"/>.</exception>
    public JpegChromaSubsampling ChromaSubsampling
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The chroma subsampling is not valid.");

            field = value;
        }
    }

    /// <summary>Gets the coding process. Defaults to <see cref="JpegEncodingProcess.Baseline"/>.</summary>
    /// <remarks>
    /// Both processes store the same quantized coefficients, so they decode to the same pixels.
    /// <see cref="JpegEncodingProcess.Progressive"/> writes them in several scans (six for a grayscale image, ten for a color
    /// one), each with Huffman tables computed from its own statistics: the DC coefficients without their lowest bit, the
    /// first five AC coefficients of the luminance without their two lowest bits, the chrominance without its lowest bit,
    /// the rest of the luminance, then the missing bits. The coefficients of the whole image are kept in memory until the
    /// last scan (two bytes per coefficient), where the baseline process only keeps a few rows.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="JpegEncodingProcess"/>.</exception>
    public JpegEncodingProcess Process
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The coding process is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the opaque background color non-opaque pixels are composited over, interpreted in the encoded color space of the
    /// image (sRGB when untagged), or <see langword="null"/> to reject non-opaque pixels. Fully opaque images never need it.
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
