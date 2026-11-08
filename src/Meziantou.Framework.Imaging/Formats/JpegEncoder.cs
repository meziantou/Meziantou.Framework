namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Immutable settings for JPEG encoding.</summary>
/// <remarks>
/// <para>
/// The output is a JPEG (JFIF) whose quantization tables are those of ITU-T T.81 Annex K scaled by
/// <see cref="Quality"/>: by default a baseline sequential one (one interleaved scan, the typical Huffman tables of
/// Annex K, or tables computed from the image with <see cref="OptimizeHuffmanTables"/>), or a progressive one
/// (<see cref="Process"/>), with restart markers every <see cref="RestartInterval"/> MCUs if set. Gray pixel formats produce a single
/// grayscale component; color pixel formats produce full-range YCbCr subsampled according to <see cref="ChromaSubsampling"/>.
/// The component layout follows the pixel format, never the pixel values.
/// </para>
/// <para>
/// <see cref="BitDepth"/> selects 12-bit samples (an extended sequential or progressive frame) and
/// <see cref="EntropyCoding"/> arithmetic coding (SOF9 or SOF10 frames). <see cref="JpegEncodingProcess.Lossless"/> writes
/// the samples exactly (SOF3, or SOF11 with arithmetic coding), with 2 to 16 bits and RGB components for color pixel
/// formats.
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
        get => _chromaSubsampling;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The chroma subsampling is not valid.");

            _chromaSubsampling = value;
        }
    }

    private JpegChromaSubsampling _chromaSubsampling;

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
    /// Gets a value indicating whether a baseline JPEG uses Huffman tables computed from the image instead of the typical
    /// tables of Annex K. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The image is coded twice from its quantized coefficients: once to count the symbols of each table, from which
    /// length-limited codes are built (ITU-T T.81 section K.2: at most 16 bits, no code made of 1-bits only), then to write
    /// the scan with them. The quantized coefficients, and so the decoded pixels, are those of the default output; only the
    /// DHT segment and the size of the entropy-coded data change, usually a few percent smaller. As with the progressive
    /// process, the coefficients of the whole image are kept in memory until the scan is written (two bytes per
    /// coefficient), where the default baseline output only keeps a few rows. It has no effect on
    /// <see cref="JpegEncodingProcess.Progressive"/>, whose scans always use tables computed from their own symbols.
    /// </remarks>
    public bool OptimizeHuffmanTables { get; init; }

    /// <summary>Gets the number of minimum coded units (MCUs) between two restart markers, or 0 (the default) for no restart marker.</summary>
    /// <remarks>
    /// <para>
    /// With a nonzero interval, a DRI segment precedes the first scan and each scan is split into intervals of that many
    /// MCUs, in raster order (ITU-T T.81 sections B.2.4.4 and F.1.2.3): every interval but the last ends with the 1-bits that
    /// complete its last byte and a marker from RST0 to RST7 (cyclically), and the next interval starts again with DC
    /// predictions of 0 (and, in a progressive scan, without a pending end-of-band run). A decoder can then resynchronize
    /// after corrupted data, or decode the intervals independently. The decoded pixels do not change: only a few bytes are
    /// added per marker, and the end-of-band runs of a progressive scan are shorter.
    /// </para>
    /// <para>
    /// An MCU of the interleaved scans covers 8x8 pixels for a grayscale image or 4:4:4 chroma, 16x8 for 4:2:2 and 16x16
    /// for 4:2:0; in the single-component scans of the progressive process, it is one 8x8 block of the component. An
    /// interval equal to the number of MCUs of a row of the baseline scan, <c>ceil(width / MCU width)</c>, puts a marker
    /// before every MCU row but the first.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 65,535.</exception>
    public int RestartInterval
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, ushort.MaxValue);
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

    /// <summary>
    /// Gets the entropy coding of the output. Defaults to <see cref="JpegEntropyCoding.Huffman"/>.
    /// </summary>
    /// <remarks>
    /// Arithmetic coding stores the coefficients (or, with <see cref="JpegEncodingProcess.Lossless"/>, the sample
    /// differences) of Huffman coding with the default conditioning of T.81 (no DAC segment), so the output decodes to the
    /// same pixels and is usually smaller. As with optimized Huffman tables, the coefficients of the whole image are kept
    /// in memory until the scans are written (two bytes per coefficient).
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="JpegEntropyCoding"/>.</exception>
    public JpegEntropyCoding EntropyCoding
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The entropy coding is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the sample precision of the output, or <see langword="null"/> (the default) for 8 bits with the DCT processes
    /// and the depth of the pixel format (8 or 16 bits) with <see cref="JpegEncodingProcess.Lossless"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The DCT processes (<see cref="JpegEncodingProcess.Baseline"/> and <see cref="JpegEncodingProcess.Progressive"/>)
    /// take 8 or 12 bits; the lossless process takes 2 to 16 bits. Samples are scaled to the precision with nearest
    /// rounding (8-bit pixels are scaled up exactly). A value below the depth of a 16-bit pixel format is the consent to
    /// reduce its samples (as <see cref="AllowBitDepthReduction"/> is for the default).
    /// </para>
    /// <para>
    /// 12-bit output is an extended sequential (SOF1) or progressive frame with the same quantization tables as 8-bit
    /// output (as libjpeg writes it), so its coefficients are quantized 16 times more finely relative to the sample range.
    /// The Huffman tables of a 12-bit or lossless frame are always computed from the image (the typical tables of
    /// Annex K do not cover their categories), and the coefficients of the whole image are kept in memory until the scan is
    /// written. Some decoders (most web browsers among them) do not read 12-bit or lossless files.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 2 and 16.</exception>
    public int? BitDepth
    {
        get;
        init
        {
            if (value is < 2 or > 16)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The bit depth must be between 2 and 16.");

            field = value;
        }
    }

    /// <summary>Gets the predictor of <see cref="JpegEncodingProcess.Lossless"/> output, 1 to 7 (T.81 table H.1). Defaults to 1.</summary>
    /// <remarks>
    /// With <c>Ra</c> the sample on the left, <c>Rb</c> the one above and <c>Rc</c> the one above on the left, the predictors
    /// are <c>Ra</c> (1), <c>Rb</c> (2), <c>Rc</c> (3), <c>Ra + Rb - Rc</c> (4), <c>Ra + ((Rb - Rc) &gt;&gt; 1)</c> (5),
    /// <c>Rb + ((Ra - Rc) &gt;&gt; 1)</c> (6) and <c>(Ra + Rb) &gt;&gt; 1</c> (7). The first line uses <c>Ra</c> and the
    /// first sample of the other lines <c>Rb</c>. The predictor that gives the smallest file depends on the image; every
    /// predictor gives the same decoded samples. The point transform is always 0: every bit of the samples is kept.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and 7.</exception>
    public int LosslessPredictor
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 7);
            field = value;
        }
    } = 1;

    /// <summary>
    /// Gets a value indicating whether pixel formats deeper than the precision of the output (16-bit pixel formats, unless
    /// <see cref="BitDepth"/> is 16 with <see cref="JpegEncodingProcess.Lossless"/>) may be reduced to it (nearest
    /// rounding) when <see cref="BitDepth"/> is not set. Defaults to <see langword="false"/>.
    /// </summary>
    public bool AllowBitDepthReduction { get; init; }

    /// <summary>
    /// Gets the gain map written next to the image, or <see langword="null"/> (the default) for none.
    /// </summary>
    /// <remarks>
    /// The file is an Ultra HDR file: the gain map image is a second JPEG image after the primary image, which a Multi-Picture
    /// Format index (APP2 <c>MPF</c>) locates. The parameters are written twice, as the metadata of ISO 21496-1 (an APP2
    /// segment in each image, the primary image only giving the version) and as the <c>hdrgm</c> XMP properties of Adobe and
    /// Ultra HDR 1.0 (in the XMP packet of the gain map image, with a container directory in the one of the primary image,
    /// merged with the XMP packet of each image when it has one), which cannot express
    /// <see cref="Metadata.GainMapMetadata.UseBaseColorSpace"/> set to <see langword="false"/>. The encoded images are kept in
    /// memory until the file is written, since the index of the primary image gives the length of both.
    /// The image must be a still image. The gain map image is encoded with the same settings but full chroma resolution
    /// (<see cref="JpegChromaSubsampling.Ratio444"/>: the gain amplifies its errors), and its metadata (the ICC profile of
    /// the alternate rendition) with <see cref="ImageEncoder.MetadataHandling"/>. A gain map is only written with 8-bit
    /// Huffman-coded output (<see cref="JpegEncodingProcess.Baseline"/> or <see cref="JpegEncodingProcess.Progressive"/>),
    /// which Ultra HDR readers decode: the image is rejected with an <see cref="ArgumentException"/> otherwise.
    /// </remarks>
    public GainMap? GainMap
    {
        get => _gainMap;
        init => _gainMap = value;
    }

    private GainMap? _gainMap;

    /// <inheritdoc />
    private protected override void PrepareGainMapImageSettings()
    {
        _gainMap = null;
        _chromaSubsampling = JpegChromaSubsampling.Ratio444;
    }
}
