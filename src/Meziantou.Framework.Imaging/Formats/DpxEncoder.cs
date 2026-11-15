namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Immutable settings for DPX encoding (SMPTE ST 268, version 2.0: one RGB, RGBA or luma image element).</summary>
/// <remarks>
/// <para>
/// The output has one uncompressed image element, after the generic and industry-specific headers, at the 8 KiB offset the
/// standard recommends. Gray pixel formats are written as a luma element (descriptor 6), opaque images as RGB (50), and
/// images that have a pixel that is not fully opaque as RGBA (51). 10-bit components are filled to 32-bit words with
/// method A (three components per word, the first in the most significant bits, two padding bits at the bottom) and 12-bit
/// components to 16-bit words (four padding bits at the bottom), the packing every reader reads; lines start on 32-bit
/// words.
/// </para>
/// <para>
/// By default, 8-bit pixel formats are written with 8 bits, 16-bit ones with 16 bits and floating-point ones as 32-bit
/// floating-point components (<see cref="RgbaHalf"/> exactly, through single precision). <see cref="BitDepth"/> selects
/// another depth: integer samples are scaled to the nearest code value, <c>round(v × (2^n - 1) / max)</c>, and samples
/// written as floating point are <c>v / max</c>. Writing 16-bit samples with fewer bits loses precision and requires
/// <see cref="AllowBitDepthReduction"/>; writing floating-point pixels as integers follows
/// <see cref="ImageEncoder.FloatingPointConversion"/>.
/// </para>
/// <para>
/// The transfer characteristic and the colorimetric specification come from the metadata: linear light
/// (<see cref="Metadata.ColorTransferFunction.Linear"/>) is written as transfer 2 (linear), and the CICP descriptions of
/// BT.709 and of the two BT.601 systems as their codes of Tables 5A and 5B; other images are written as user defined
/// (transfer 0) with the primaries of BT.709 (colorimetric 6), the primaries of sRGB. Another CICP description or an ICC
/// profile, EXIF, XMP, a resolution and text entries follow <see cref="ImageEncoder.MetadataHandling"/> (rejected by default).
/// The orientation is written as the orientation code of the image header and the pixel aspect ratio in its field;
/// <see cref="Metadata.ImageMetadata.DpxHeader"/> is ignored.
/// </para>
/// <para>
/// DPX stores exactly one frame: animated images and images with a poster frame are rejected with an
/// <see cref="UnsupportedImageFeatureException"/>; use <see cref="ImageFrame.Save(string, ImageEncoder?)"/> to save one frame
/// explicitly.
/// </para>
/// </remarks>
public sealed class DpxEncoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Dpx;

    /// <summary>
    /// Gets the number of bits of the components: 8, 10, 12 or 16 (integers) or 32 (floating point), or
    /// <see langword="null"/> (the default) for the depth of the pixel format.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not 8, 10, 12, 16 or 32.</exception>
    public int? BitDepth
    {
        get;
        init
        {
            if (value is not (null or 8 or 10 or 12 or 16 or 32))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The DPX bit depth is 8, 10, 12, 16 or 32.");

            field = value;
        }
    }

    /// <summary>
    /// Gets a value indicating whether 16-bit pixels may be written with fewer bits (<see cref="BitDepth"/> set to 8, 10
    /// or 12), which rounds them. Defaults to <see langword="false"/>.
    /// </summary>
    public bool AllowBitDepthReduction { get; init; }

    /// <summary>Gets the byte order of the file. Defaults to <see cref="DpxByteOrder.BigEndian"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="DpxByteOrder"/>.</exception>
    public DpxByteOrder ByteOrder
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The byte order is not valid.");

            field = value;
        }
    }

    /// <summary>Gets a value indicating whether floating-point samples are stored without reduction to integer pixels: when <see cref="BitDepth"/> is not an integer depth.</summary>
    internal override bool StoresFloatingPointSamples => BitDepth is null or 32;

    /// <summary>Gray and 16-bit RGB pixels are written as luma and RGB elements, not widened to RGBA; the floating-point gray formats without alpha as floating-point luma elements.</summary>
    internal override bool StoresPixelFormat(PixelFormat format) => format is PixelFormat.Rgb48 or PixelFormat.GrayFloat or PixelFormat.GrayHalf || base.StoresPixelFormat(format);
}
