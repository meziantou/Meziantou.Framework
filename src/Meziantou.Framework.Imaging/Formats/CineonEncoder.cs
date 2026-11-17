namespace Meziantou.Framework.Imaging.Formats;

/// <summary>
/// Immutable settings for Kodak Cineon encoding (Image File Format Proposal for Digital Pictures, draft 4.5): film scans of
/// one gray or three RGB channels.
/// </summary>
/// <remarks>
/// <para>
/// The file is big-endian (magic number <c>802A5FD7</c>), with the generic and motion-picture sections of the header
/// (2,048 bytes) followed by the image data: the channels interleaved by pixel, unsigned, with packing 5 (32-bit cells,
/// left-justified), the packing of the 10-bit film scans. A 10-bit RGB pixel is one 32-bit word (red in its 10 most
/// significant bits, then green and blue, two bits of padding at the bottom); 10-bit gray samples share the words, three
/// per word. 8 and 16-bit samples are written as a stream of bytes and of 16-bit words, every line ending on a 32-bit word,
/// as ImageMagick writes and reads them.
/// </para>
/// <para>
/// Gray pixel formats are written as one channel, the others as three; Cineon has no alpha channel, so a pixel that is not
/// fully opaque is rejected with an <see cref="UnsupportedImageFeatureException"/> (flatten the image first), and CMYK pixels
/// are not stored. By default, 8-bit pixel formats are written with 10 bits, which keeps their values, and 16-bit ones with
/// 16 bits. <see cref="BitDepth"/> selects another depth: samples are scaled to the nearest code value,
/// <c>round(v × (2^n - 1) / max)</c>; writing 16-bit samples with fewer bits requires <see cref="AllowBitDepthReduction"/>.
/// Floating-point pixels follow <see cref="ImageEncoder.FloatingPointConversion"/>.
/// </para>
/// <para>
/// The code values are written as they are: Cineon files usually hold printing densities, and the encoder converts
/// nothing. The fields of <see cref="Metadata.ImageMetadata.CineonHeader"/> that describe the film and its scan are copied
/// to the header (the label, the file and source names and dates, the input device, the film edge code, format and frame,
/// the offsets, the pitch, the gamma, the chromaticities, the image sense), and so are the channel designators and the
/// minimum and maximum data values and quantities when the image has as many channels and the header as many bits
/// (otherwise the designators of the printing densities of the universal metric, and the full range of the code values for
/// the densities 0 to 2.048). The orientation is written as the orientation code. An ICC profile, a CICP description other
/// than one equivalent to sRGB, a linear label, EXIF, XMP, a resolution and text entries follow
/// <see cref="ImageEncoder.MetadataHandling"/> (rejected by default).
/// </para>
/// <para>
/// A Cineon file stores exactly one frame: animated images and images with a poster frame are rejected with an
/// <see cref="UnsupportedImageFeatureException"/>; use <see cref="ImageFrame.Save(string, ImageEncoder?)"/> to save one frame
/// explicitly.
/// </para>
/// </remarks>
public sealed class CineonEncoder : ImageEncoder
{
    /// <inheritdoc />
    public override ImageFormat Format => ImageFormat.Cineon;

    /// <summary>
    /// Gets the number of bits of the samples: 8, 10 or 16, or <see langword="null"/> (the default) for 10 bits with 8-bit
    /// pixel formats and 16 bits with 16-bit ones.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not 8, 10 or 16.</exception>
    public int? BitDepth
    {
        get;
        init
        {
            if (value is not (null or 8 or 10 or 16))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The Cineon bit depth is 8, 10 or 16.");

            field = value;
        }
    }

    /// <summary>
    /// Gets a value indicating whether 16-bit pixels may be written with fewer bits (<see cref="BitDepth"/> set to 8 or 10),
    /// which rounds them. Defaults to <see langword="false"/>.
    /// </summary>
    public bool AllowBitDepthReduction { get; init; }

    /// <summary>Gray with alpha and 16-bit RGB pixels are written as gray and RGB channels, not widened to RGBA.</summary>
    internal override bool StoresPixelFormat(PixelFormat format) => format is PixelFormat.La16 or PixelFormat.La32 or PixelFormat.Rgb48 || base.StoresPixelFormat(format);
}
