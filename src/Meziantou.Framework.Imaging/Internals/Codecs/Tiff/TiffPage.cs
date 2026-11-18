using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// One resolved and validated TIFF page: the geometry, the sample layout, the compression, the strip or tile table and the
/// metadata of a single image file directory. A page is resolved without reading a single
/// pixel byte, so a collection can describe every page of a document without decoding any of them.
/// </summary>
/// <remarks>
/// <para>
/// The supported subset is checked here, once: unsigned integer samples; grayscale (<c>WhiteIsZero</c> and
/// <c>BlackIsZero</c>, 1, 2, 4, 8 or 16 bits), RGB (8 or 16 bits), palette color (1, 2, 4 or 8 bits), CMYK (8 or 16 bits)
/// YCbCr (8 bits) and L*a*b* (<c>CIELab</c> and <c>ITULab</c> of 8 or 16 bits, <c>ICCLab</c> of 8 bits, lightness only or
/// with a* and b*) data; signed integer grayscale and RGB samples of 8 or 16 bits; floating-point <c>BlackIsZero</c>
/// grayscale and RGB samples of 16 (half precision), 24 or 32 bits; chunky and separate planar configurations (separate
/// planes of subsampled YCbCr data with the lossless compressions only); strips or tiles; no
/// compression, Deflate, LZW, PackBits, the CCITT facsimile compressions, JPEG, old-style JPEG and JPEG 2000; the horizontal differencing
/// predictor, and the floating-point predictor of TIFF Technical Note 3. Everything else is recognized and rejected with
/// <see cref="UnsupportedImageFeatureException"/>, never guessed.
/// </para>
/// <para>
/// A signed sample is stored as the unsigned sample of the same width whose value is larger by half of the range
/// (<c>-2^(n-1)</c> becomes 0, 0 becomes <c>2^(n-1)</c> and <c>2^(n-1) - 1</c> the largest value): the whole range of
/// the signed type covers the whole range of the unsigned one, exactly. The block reader flips the sign bit of each
/// sample (<see cref="TiffBlockReader"/>), so the rest of the decoder only sees unsigned samples.
/// </para>
/// <para>
/// Floating-point samples are linear light (<see cref="ColorTransferFunction.Linear"/>, unless an ICC profile describes
/// them) and are kept as they are stored: half-precision samples in <see cref="PixelFormat.RgbaHalf"/>, 24- and 32-bit
/// ones in <see cref="PixelFormat.RgbaFloat"/>, which holds both exactly.
/// </para>
/// <para>
/// A page compressed with JPEG stores the samples of its datastreams, which have 8 to 16 bits (12 bits for the
/// DCT-based processes, any of them for the lossless one): <see cref="BitsPerSample"/> is their precision, and the
/// samples of more than 8 bits are decoded scaled to 16 bits (<see cref="DecodedBitsPerSample"/>), as the ones of a
/// JPEG file are.
/// </para>
/// <para>
/// Alpha is only an alpha channel when <c>ExtraSamples</c> says so and says it is unassociated (straight): the working
/// pixel formats store straight alpha, so associated (premultiplied) alpha is rejected instead of being silently divided
/// out, and an unspecified extra sample is not transparency.
/// </para>
/// <para>
/// CMYK samples are inks, described here like any other samples (an identification needs nothing else): their pixels are
/// decoded as CMYK pixels or with a color conversion (<see cref="TiffPageDecoder"/>).
/// </para>
/// </remarks>
internal sealed class TiffPage
{
    private readonly bool _isLenient;

    private TiffPage(TiffDirectory directory, int index, bool isLenient)
    {
        Directory = directory;
        Index = index;
        _isLenient = isLenient;
    }

    /// <summary>Gets the directory this page was resolved from.</summary>
    public TiffDirectory Directory { get; }

    /// <summary>Gets the zero-based index of the page in the document.</summary>
    public int Index { get; }

    /// <summary>Gets a value indicating whether the samples are stored in big-endian byte order.</summary>
    public bool IsBigEndian => Directory.Header.IsBigEndian;

    /// <summary>Gets the page size, in pixels.</summary>
    public Size Size { get; private set; }

    /// <summary>
    /// Gets the number of bits of one sample, as the page declares it: 1, 2, 4, 8 or 16, 9 to 15 for a page compressed with
    /// JPEG, or 24 and 32 for floating-point samples.
    /// </summary>
    public int BitsPerSample { get; private set; }

    /// <summary>
    /// Gets the number of bits of one sample of a decoded block: <see cref="BitsPerSample"/>, or 16 for the samples of 9
    /// to 15 bits of a JPEG datastream, which its decoder scales to 16 bits (or gives as they are stored, in 16 bits, for
    /// a DNG raw image). The other samples of 9 to 15 bits of a DNG raw image stay packed.
    /// </summary>
    public int DecodedBitsPerSample => IsJpeg && BitsPerSample is > 8 and < 16 ? 16 : BitsPerSample;

    /// <summary>
    /// Gets the format of the samples: <see cref="TiffFormat.SampleFormatUnsignedInteger"/> (also for an undefined
    /// format), <see cref="TiffFormat.SampleFormatSignedInteger"/> or <see cref="TiffFormat.SampleFormatFloat"/>.
    /// </summary>
    public int SampleFormat { get; private set; } = TiffFormat.SampleFormatUnsignedInteger;

    /// <summary>Gets a value indicating whether the samples are two's complement signed integers.</summary>
    public bool IsSigned => SampleFormat == TiffFormat.SampleFormatSignedInteger;

    /// <summary>Gets a value indicating whether the samples are IEEE floating-point numbers (16, 24 or 32 bits).</summary>
    public bool IsFloatingPoint => SampleFormat == TiffFormat.SampleFormatFloat;

    /// <summary>Gets the number of samples of one pixel: the color samples of the photometric interpretation, and at most one alpha sample.</summary>
    public int SamplesPerPixel { get; private set; }

    /// <summary>Gets the number of bits of one pixel of a decoded block.</summary>
    public int BitsPerPixel => SamplesPerPixel * DecodedBitsPerSample;

    /// <summary>Gets the photometric interpretation (0, 1, 2, 3, 5, 6, 8, 9 or 10).</summary>
    public int Photometric { get; private set; }

    /// <summary>Gets a value indicating whether 0 is white (<c>WhiteIsZero</c>): the decoder inverts the samples.</summary>
    public bool IsWhiteIsZero => Photometric == TiffFormat.PhotometricWhiteIsZero;

    /// <summary>Gets a value indicating whether the samples are grayscale.</summary>
    public bool IsGrayscale => Photometric is TiffFormat.PhotometricWhiteIsZero or TiffFormat.PhotometricBlackIsZero;

    /// <summary>Gets a value indicating whether the samples are indexes into <see cref="ColorMap"/>.</summary>
    public bool IsPalette => Photometric == TiffFormat.PhotometricPalette;

    /// <summary>Gets a value indicating whether the samples are cyan, magenta, yellow and black ink amounts.</summary>
    public bool IsCmyk => Photometric == TiffFormat.PhotometricCmyk;

    /// <summary>Gets a value indicating whether the samples are luma and chroma.</summary>
    public bool IsYCbCr => Photometric == TiffFormat.PhotometricYCbCr;

    /// <summary>
    /// Gets a value indicating whether the samples are CIE L*a*b* colors: <c>CIELab</c> (signed a* and b*),
    /// <c>ICCLab</c> (a* and b* offset by half of their range, as ICC profiles encode them) or <c>ITULab</c> (each sample
    /// a linear encoding of a range of its component, <see cref="ItuLabRanges"/>).
    /// </summary>
    public bool IsCieLab => Photometric is TiffFormat.PhotometricCieLab or TiffFormat.PhotometricIccLab or TiffFormat.PhotometricItuLab;

    /// <summary>
    /// Gets a value indicating whether an L*a*b* page stores the lightness only (one color sample, TIFF 6.0 section 23):
    /// its colors are neutral, a* and b* are 0.
    /// </summary>
    public bool IsLightnessOnly => IsCieLab && SamplesPerPixel - (HasAlpha ? 1 : 0) == 1;

    /// <summary>
    /// Gets the ranges of L*, a* and b* that the samples of an <c>ITULab</c> page encode (<c>Decode</c>, RFC 2301 section
    /// 6.2.3): the minimum and the maximum of each component, the value of the samples 0 and <c>2^n - 1</c>. The defaults
    /// are 0 to 100 for L*, -85 to 85 for a* and -75 to 125 for b* (ITU-T T.42). Empty for the other pages.
    /// </summary>
    public double[] ItuLabRanges { get; private set; } = [];

    /// <summary>
    /// Gets the chromaticity of the white the L*a*b* colors of the page are relative to (<c>WhitePoint</c>), or
    /// <see langword="null"/> for D50, which Adobe's TIFF technical note gives as the default (TIFF 6.0 gives none).
    /// </summary>
    public (double X, double Y)? WhitePoint { get; private set; }

    /// <summary>Gets a value indicating whether the last sample is a straight alpha channel.</summary>
    public bool HasAlpha { get; private set; }

    /// <summary>
    /// Gets the color map of a palette page: the red values of every index, then the green ones, then the blue ones, each
    /// between 0 and 65,535.
    /// </summary>
    public ushort[] ColorMap { get; private set; } = [];

    /// <summary>
    /// Gets a value indicating whether the color map holds colors that 8 bits cannot store: its entries are neither all
    /// multiples of 257 nor all multiples of 256, the two ways writers store 8-bit colors in the 16-bit entries.
    /// </summary>
    public bool HasSixteenBitColorMap { get; private set; }

    /// <summary>Gets the divisor that turns an entry of an 8-bit color map into its 8-bit color: 257 or 256.</summary>
    public int ColorMapDivisor { get; private set; } = 257;

    /// <summary>Gets the conversion and the subsampling of a YCbCr page, or <see langword="null"/>.</summary>
    public TiffYCbCrConverter? YCbCr { get; private set; }

    /// <summary>
    /// Gets the conversion of the data units the blocks store, or <see langword="null"/>: the one of a YCbCr page with
    /// chunky samples. The blocks of a page of separate YCbCr planes hold one sample per pixel, which the expander converts
    /// once the planes are interleaved (<see cref="TiffRowExpander"/>).
    /// </summary>
    public TiffYCbCrConverter? BlockYCbCr => IsPlanar ? null : YCbCr;

    /// <summary>
    /// Gets the conversion of the interleaved planes of a planar YCbCr page, or <see langword="null"/>: each pixel is a data
    /// unit of its own, the samples of subsampled chroma planes being replicated over the pixels they cover.
    /// </summary>
    public TiffYCbCrConverter? PlaneYCbCr { get; private set; }

    /// <summary>Gets the compression (1, 2, 3, 4, 5, 7, 8, 32773 or 32946).</summary>
    public int Compression { get; private set; }

    /// <summary>Gets a value indicating whether the strips or tiles are Deflate compressed.</summary>
    public bool IsDeflate => Compression is TiffFormat.CompressionAdobeDeflate or TiffFormat.CompressionDeflate;

    /// <summary>Gets a value indicating whether the strips or tiles use one of the CCITT facsimile compressions.</summary>
    public bool IsCcitt => Compression is TiffFormat.CompressionCcittRle or TiffFormat.CompressionCcittFax3 or TiffFormat.CompressionCcittFax4;

    /// <summary>Gets a value indicating whether each strip or tile is a JPEG datastream.</summary>
    public bool IsJpeg => Compression is TiffFormat.CompressionJpeg or TiffFormat.CompressionOldJpeg or TiffFormat.CompressionDngLossyJpeg;

    /// <summary>
    /// Gets a value indicating whether the page is the raw image of a DNG file (<see cref="ResolveDngRaw"/>): its samples
    /// are the values of the sensor, which only the DNG pipeline interprets; nothing expands them into pixels.
    /// </summary>
    public bool IsDngRaw { get; private set; }

    /// <summary>
    /// Gets the distance, in pixels, of the sample a predictor of the page differences with: 1, or 2 and 4 for the
    /// predictors of DNG (<c>Horizontal Difference X2</c> and <c>X4</c>, <c>Floating Point X2</c> and <c>X4</c>).
    /// </summary>
    public int PredictorDistance { get; private set; } = 1;

    /// <summary>Gets a value indicating whether the page uses a horizontal differencing predictor.</summary>
    public bool HasHorizontalDifferencing => Predictor is TiffFormat.PredictorHorizontalDifferencing or TiffFormat.PredictorHorizontalDifferencingX2 or TiffFormat.PredictorHorizontalDifferencingX4;

    /// <summary>Gets a value indicating whether the page uses a floating-point predictor.</summary>
    public bool HasFloatingPointPredictor => Predictor is TiffFormat.PredictorFloatingPoint or TiffFormat.PredictorFloatingPointX2 or TiffFormat.PredictorFloatingPointX4;

    /// <summary>Gets a value indicating whether the page uses the old-style JPEG compression of TIFF 6.0 (<see cref="OldJpeg"/>).</summary>
    public bool IsOldJpeg => Compression == TiffFormat.CompressionOldJpeg;

    /// <summary>Gets how the blocks of an old-style JPEG page become JPEG datastreams, or <see langword="null"/> for the other pages.</summary>
    public TiffOldJpegLayout? OldJpeg { get; private set; }

    /// <summary>Gets a value indicating whether each block holds a JPEG 2000 codestream.</summary>
    public bool IsJpeg2000 => Compression == TiffFormat.CompressionJpeg2000;

    /// <summary>Gets a value indicating whether a Group 3 page mixes one- and two-dimensional rows (<c>T4Options</c> bit 0).</summary>
    public bool IsTwoDimensionalFax { get; private set; }

    /// <summary>Gets the tables shared by the JPEG datastreams of the page (<c>JPEGTables</c>), or <see langword="null"/>.</summary>
    public byte[]? JpegTables { get; private set; }

    /// <summary>Gets a value indicating whether the bits of each stored byte are in reverse order (<c>FillOrder</c> 2).</summary>
    public bool IsLeastSignificantBitFirst { get; private set; }

    /// <summary>Gets the predictor (1 or 2).</summary>
    public int Predictor { get; private set; }

    /// <summary>
    /// Gets a value indicating whether each sample of a pixel is stored in a plane of its own (<c>PlanarConfiguration</c>
    /// 2 with several samples per pixel): the blocks of the first sample, then the ones of the second sample, and so on.
    /// Each block holds one sample per pixel; the decoder interleaves the planes back into rows of whole pixels.
    /// </summary>
    public bool IsPlanar { get; private set; }

    /// <summary>Gets the number of planes: <see cref="SamplesPerPixel"/> for a planar page, 1 otherwise.</summary>
    public int PlaneCount => IsPlanar ? SamplesPerPixel : 1;

    /// <summary>Gets the number of samples of a pixel in one block: 1 for a planar page, <see cref="SamplesPerPixel"/> otherwise.</summary>
    public int BlockSamplesPerPixel => IsPlanar ? 1 : SamplesPerPixel;

    /// <summary>Gets the number of blocks of one plane: <see cref="BlocksAcross"/> times <see cref="BlocksDown"/>.</summary>
    public int BlocksPerPlane => BlocksAcross * BlocksDown;

    /// <summary>Gets a value indicating whether the page is stored as tiles rather than strips.</summary>
    public bool IsTiled { get; private set; }

    /// <summary>Gets the width of one tile, or the page width for strips.</summary>
    public int BlockWidth { get; private set; }

    /// <summary>Gets the number of rows of one tile or strip (the last strip may be shorter; a tile is never shorter).</summary>
    public int BlockHeight { get; private set; }

    /// <summary>Gets the number of blocks per row of blocks (1 for strips).</summary>
    public int BlocksAcross { get; private set; }

    /// <summary>Gets the number of rows of blocks.</summary>
    public int BlocksDown { get; private set; }

    /// <summary>Gets the file offset of each strip or tile, in reading order (every block of a plane before the next plane).</summary>
    public ulong[] BlockOffsets { get; private set; } = [];

    /// <summary>Gets the stored length of each strip or tile, in reading order.</summary>
    public ulong[] BlockByteCounts { get; private set; } = [];

    /// <summary>
    /// Gets the number of bytes of one full row of a decoded block, padded to a whole number of bytes. The rows of a
    /// YCbCr page are the RGB samples its data units convert to; the rows of a block of a planar page hold one sample per pixel.
    /// </summary>
    public int BlockRowLength { get; private set; }

    /// <summary>Gets the page metadata: resolution, orientation, ICC profile and XMP packet.</summary>
    public ImageMetadata Metadata { get; private set; } = new();

    /// <summary>Gets the layout of the expanded rows: <see cref="DefaultPixelFormat"/>, as RGBA for gray with alpha and 16-bit RGB.</summary>
    public PixelFormat SourcePixelFormat => PixelFormatWidening.GetWidenedFormat(DefaultPixelFormat);

    /// <summary>Gets the lossless working representation of the samples.</summary>
    public PixelFormat DefaultPixelFormat
    {
        get
        {
            // The raw samples of a DNG file become pixels in the DNG pipeline only
            if (IsDngRaw)
                return PixelFormat.RgbaFloat;

            if (IsPalette)
                return HasSixteenBitColorMap ? PixelFormat.Rgb48 : PixelFormat.Rgb24;

            if (IsFloatingPoint)
                return DefaultPixelFormats.ForTiffFloatingPoint(IsGrayscale, HasAlpha, BitsPerSample);

            return DefaultPixelFormats.ForTiff(IsGrayscale, HasAlpha, DecodedBitsPerSample == 16);
        }
    }

    /// <summary>Gets the color model reported by identification.</summary>
    public ImageColorModel ColorModel => Photometric switch
    {
        TiffFormat.PhotometricPalette => ImageColorModel.Indexed,
        TiffFormat.PhotometricCmyk => ImageColorModel.Cmyk,
        TiffFormat.PhotometricYCbCr => ImageColorModel.YCbCr,
        TiffFormat.PhotometricCieLab or TiffFormat.PhotometricIccLab or TiffFormat.PhotometricItuLab => ImageColorModel.CieLab,
        TiffFormat.PhotometricRgb => HasAlpha ? ImageColorModel.Rgba : ImageColorModel.Rgb,
        _ => HasAlpha ? ImageColorModel.GrayscaleAlpha : ImageColorModel.Grayscale,
    };

    /// <summary>Resolves and validates one page.</summary>
    /// <param name="directory">The directory of the page.</param>
    /// <param name="index">The zero-based index of the page.</param>
    /// <param name="context">The operation context (limits and metadata accounting).</param>
    /// <param name="readMetadata">
    /// Whether the metadata of the page (resolution, orientation, ICC profile, XMP) is read and charged; otherwise
    /// <see cref="Metadata"/> is empty. A reduced-resolution image of a page (<see cref="TiffResolutionLevels"/>) is decoded
    /// with the metadata of the page.
    /// </param>
    /// <returns>The page.</returns>
    /// <exception cref="InvalidImageContentException">A required tag is missing, or a tag is inconsistent with the rest of the page.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The page uses a recognized but unsupported variant.</exception>
    /// <exception cref="ImageResourceLimitException">The page exceeds a configured limit.</exception>
    public static TiffPage Resolve(TiffDirectory directory, int index, ImageCodecContext context, bool readMetadata = true)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(context);
        var page = new TiffPage(directory, index, context.IsLenient);
        page.ResolveGeometry(context);
        page.ResolveSamples();
        page.ResolveCompression();
        page.ResolveBlocks(context);
        if (readMetadata)
        {
            page.ResolveMetadata(context);
        }

        return page;
    }

    /// <summary>
    /// Resolves and validates the raw image of a DNG file: its geometry, its strips or tiles and how they are stored, as
    /// the DNG specification 1.7.1 restricts and extends the TIFF tags (chapter 3). The samples are those of a color filter
    /// array (<c>CFA</c>, one per pixel) or of linear raw data (<c>LinearRaw</c>, one to four per pixel): unsigned integers
    /// of 8 to 16 bits (packed most significant bit first when they are not 8 or 16 bits) or floating-point numbers of 16,
    /// 24 or 32 bits; uncompressed, Deflate (with the predictors of DNG), lossless JPEG (whose frames only need to hold as
    /// many samples as the block), or lossy JPEG of 8-bit linear raw data. The metadata is the one of the DNG file, which
    /// the DNG decoder reads: the page has none.
    /// </summary>
    /// <param name="directory">The raw IFD.</param>
    /// <param name="index">The index of the page, for messages.</param>
    /// <param name="context">The operation context.</param>
    /// <returns>The page.</returns>
    /// <exception cref="InvalidImageContentException">A required tag is missing, or a tag is inconsistent with the rest of the image.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The raw image uses a recognized but unsupported variant.</exception>
    /// <exception cref="ImageResourceLimitException">The raw image exceeds a configured limit.</exception>
    public static TiffPage ResolveDngRaw(TiffDirectory directory, int index, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(context);
        var page = new TiffPage(directory, index, context.IsLenient) { IsDngRaw = true };
        page.ResolveGeometry(context);
        page.ResolveRawSamples();
        page.ResolveRawCompression();
        page.ResolveBlocks(context);
        return page;
    }

    /// <summary>Gets the number of bytes of one full row of the decoded page, padded to a whole number of bytes.</summary>
    public long RowLength => (((long)Size.Width * BitsPerPixel) + 7) / 8;

    /// <summary>
    /// Gets the index of the block decoded at a position of the decoding order. The decoder reads the blocks of one place
    /// of the page together, one plane after the other, so that it can interleave their samples: the strip or tile
    /// <c>position / PlaneCount</c> of the plane <c>position % PlaneCount</c>. Without planes, the order is the one of the blocks.
    /// </summary>
    /// <param name="position">The position, from 0 to the number of blocks.</param>
    /// <returns>The index of the block in <see cref="BlockOffsets"/>.</returns>
    public int GetBlockIndex(int position) => IsPlanar ? ((position % PlaneCount) * BlocksPerPlane) + (position / PlaneCount) : position;

    /// <summary>Gets the row of blocks (the strip, or the row of tiles) a block belongs to, whatever its plane.</summary>
    /// <param name="index">The index of the block in <see cref="BlockOffsets"/>.</param>
    /// <returns>The row of blocks, from 0 to <see cref="BlocksDown"/>.</returns>
    public int GetBlockRow(int index) => (index % BlocksPerPlane) / BlocksAcross;

    /// <summary>
    /// Gets a value indicating whether the page stores subsampled YCbCr data in separate planes: its chroma planes have
    /// fewer samples than the page has pixels (<see cref="GetPlaneSubsampling"/>).
    /// </summary>
    public bool HasSubsampledPlanes => IsPlanar && YCbCr is { } conversion && (conversion.HorizontalSubsampling > 1 || conversion.VerticalSubsampling > 1);

    /// <summary>
    /// Gets the subsampling of the plane a block belongs to. A block of a chroma plane of subsampled YCbCr data (TIFF 6.0
    /// section 21) covers the area of the blocks of the other planes with one sample for <c>h</c> x <c>v</c> pixels: its
    /// rows have <c>ceil(BlockWidth / h)</c> samples, and it has <c>ceil(rows / v)</c> rows.
    /// </summary>
    /// <param name="index">The index of the block in <see cref="BlockOffsets"/>.</param>
    /// <returns>The horizontal and vertical subsampling: (1, 1) for the other blocks.</returns>
    public (int Horizontal, int Vertical) GetPlaneSubsampling(int index)
        => HasSubsampledPlanes && index / BlocksPerPlane is 1 or 2 ? (YCbCr!.HorizontalSubsampling, YCbCr.VerticalSubsampling) : (1, 1);

    private void ResolveGeometry(ImageCodecContext context)
    {
        var width = GetRequiredUnsigned(TiffFormat.ImageWidthTag, "ImageWidth");
        var height = GetRequiredUnsigned(TiffFormat.ImageLengthTag, "ImageLength");
        if (width == 0 || height == 0)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} is {width}x{height}; both dimensions must be positive."));

        if (width > int.MaxValue || height > int.MaxValue)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} is {width}x{height}, which exceeds the largest supported canvas."));

        context.Limits.EnsureCanvasWithinLimits((int)width, (int)height);
        Size = new Size((int)width, (int)height);
    }

    private void ResolveSamples()
    {
        var samplesPerPixel = Directory.TryGetUnsigned(TiffFormat.SamplesPerPixelTag, out var samples) ? samples : 1;
        if (samplesPerPixel is 0 or > 5)
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} stores {samplesPerPixel} samples per pixel; the color samples of the photometric interpretation (1, 3 or 4) and at most one alpha sample are supported."), "SamplesPerPixel");

        SamplesPerPixel = (int)samplesPerPixel;

        // The default of TIFF is one bit per sample: bilevel data. Leniently, one value is the width of every sample, as
        // libtiff, Pillow and Apple ImageIO read it
        var bits = TryReadPerSampleValues(TiffFormat.BitsPerSampleTag) ?? [.. Enumerable.Repeat(1UL, SamplesPerPixel)];
        if (_isLenient && bits.Length == 1 && SamplesPerPixel > 1)
        {
            bits = [.. Enumerable.Repeat(bits[0], SamplesPerPixel)];
        }

        if (bits.Length != SamplesPerPixel)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} declares {bits.Length} BitsPerSample values for {SamplesPerPixel} samples per pixel."));

        foreach (var value in bits)
        {
            if (value != bits[0])
                throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} mixes sample widths ({string.Join(", ", bits)} bits); every sample must have the same width."), "BitsPerSample: mixed widths");
        }

        SampleFormat = ResolveSampleFormat();

        // The samples of a JPEG datastream have the precision of its frame: 12 bits, or any number of them when it is lossless
        var isJpeg = Directory.TryGetUnsigned(TiffFormat.CompressionTag, out var compression) && compression is TiffFormat.CompressionJpeg or TiffFormat.CompressionOldJpeg;
        switch (SampleFormat)
        {
            case TiffFormat.SampleFormatFloat when bits[0] is not (16 or 24 or 32):
                throw UnsupportedBitsPerSample(bits[0], "floating-point samples are supported with 16 (half precision), 24 and 32 bits");

            case TiffFormat.SampleFormatSignedInteger when bits[0] is not (8 or 16):
                throw UnsupportedBitsPerSample(bits[0], "signed integer samples are supported with 8 and 16 bits");

            case TiffFormat.SampleFormatUnsignedInteger when bits[0] is not (1 or 2 or 4 or 8 or 16) && !(isJpeg && bits[0] is > 8 and < 16):
                throw UnsupportedBitsPerSample(bits[0], "1, 2, 4, 8 and 16 bits are supported, and 9 to 15 bits with JPEG compression");
        }

        BitsPerSample = (int)bits[0];

        if (!Directory.TryGetUnsigned(TiffFormat.PhotometricInterpretationTag, out var photometric))
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} has no PhotometricInterpretation tag, which is required."));

        if (photometric is not (TiffFormat.PhotometricWhiteIsZero or TiffFormat.PhotometricBlackIsZero or TiffFormat.PhotometricRgb or TiffFormat.PhotometricPalette or TiffFormat.PhotometricCmyk or TiffFormat.PhotometricYCbCr or TiffFormat.PhotometricCieLab or TiffFormat.PhotometricIccLab or TiffFormat.PhotometricItuLab))
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses the {TiffFormat.GetPhotometricName((long)photometric)} photometric interpretation; WhiteIsZero, BlackIsZero, RGB, palette color, CMYK, YCbCr, CIE L*a*b*, ICC L*a*b* and ITU L*a*b* are supported."),string.Create(CultureInfo.InvariantCulture, $"PhotometricInterpretation: {photometric}"));

        Photometric = (int)photometric;
        var isSampleFormatSupported = SampleFormat switch
        {
            // Signed samples are read as unsigned ones, which WhiteIsZero then inverts; floating-point ones have no maximum to invert from
            TiffFormat.SampleFormatSignedInteger => Photometric is TiffFormat.PhotometricWhiteIsZero or TiffFormat.PhotometricBlackIsZero or TiffFormat.PhotometricRgb,
            TiffFormat.SampleFormatFloat => Photometric is TiffFormat.PhotometricBlackIsZero or TiffFormat.PhotometricRgb,
            _ => true,
        };

        if (!isSampleFormatSupported)
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} stores {GetSampleFormatName()} samples with the {TiffFormat.GetPhotometricName(Photometric)} photometric interpretation; they are supported with grayscale and RGB data{(IsFloatingPoint ? " (BlackIsZero grayscale only)" : "")}."), string.Create(CultureInfo.InvariantCulture, $"SampleFormat: {SampleFormat} with PhotometricInterpretation {Photometric}"));

        switch (Photometric)
        {
            case TiffFormat.PhotometricRgb or TiffFormat.PhotometricCmyk or TiffFormat.PhotometricCieLab or TiffFormat.PhotometricIccLab or TiffFormat.PhotometricItuLab when BitsPerSample < 8:
                throw UnsupportedBitsPerSample(bits[0], string.Create(CultureInfo.InvariantCulture, $"{TiffFormat.GetPhotometricName(Photometric)} data is supported with 8 and 16 bits"));

            // The 16-bit encodings of ICC L*a*b* differ between the versions of the ICC specification, and nothing tells them apart
            case TiffFormat.PhotometricIccLab when BitsPerSample != 8:
                throw UnsupportedBitsPerSample(bits[0], "ICC L*a*b* data is supported with 8 bits");

            case TiffFormat.PhotometricPalette when BitsPerSample > 8:
                throw UnsupportedBitsPerSample(bits[0], "palette color data is supported with 1, 2, 4 and 8 bits");

            case TiffFormat.PhotometricYCbCr when BitsPerSample != 8 && !(isJpeg && BitsPerSample > 8):
                throw UnsupportedBitsPerSample(bits[0], "YCbCr data is supported with 8 bits, and with 9 to 16 bits with JPEG compression");
        }

        // An L*a*b* page may store the lightness only (TIFF 6.0 section 23): one color sample, then the alpha sample
        var colorSamples = Photometric switch
        {
            TiffFormat.PhotometricRgb or TiffFormat.PhotometricYCbCr => 3,
            TiffFormat.PhotometricCieLab or TiffFormat.PhotometricIccLab or TiffFormat.PhotometricItuLab => SamplesPerPixel < 3 ? 1 : 3,
            TiffFormat.PhotometricCmyk => 4,
            _ => 1,
        };

        if (SamplesPerPixel < colorSamples)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} declares the {TiffFormat.GetPhotometricName(Photometric)} photometric interpretation but only {SamplesPerPixel} sample(s) per pixel."));

        var extraCount = SamplesPerPixel - colorSamples;
        if (extraCount > 1)
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} stores {extraCount} extra samples; at most one (a straight alpha channel) is supported."), "ExtraSamples: several extra channels");

        if (extraCount == 1)
        {
            var extra = Directory.TryReadUnsignedArray(TiffFormat.ExtraSamplesTag, 1);
            if (extra is not { Length: 1 })
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} stores {SamplesPerPixel} samples per pixel without an ExtraSamples tag describing the extra channel."));

            HasAlpha = extra[0] switch
            {
                TiffFormat.ExtraSampleUnassociatedAlpha => true,
                TiffFormat.ExtraSampleAssociatedAlpha => throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} stores associated (premultiplied) alpha; the working pixel formats store straight alpha and this version never divides it out."), "ExtraSamples: associated alpha"),
                TiffFormat.ExtraSampleUnspecified => throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} stores an unspecified extra sample, which is not transparency and cannot be interpreted."), "ExtraSamples: unspecified"),
                _ => throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} declares the unknown extra sample type {extra[0]}."), string.Create(CultureInfo.InvariantCulture, $"ExtraSamples: {extra[0]}")),
            };

            if (BitsPerSample < 8 || IsPalette || IsYCbCr)
                throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} stores an alpha channel with {BitsPerSample}-bit {TiffFormat.GetPhotometricName(Photometric)} data; alpha is supported with 8- and 16-bit grayscale, RGB, CMYK and L*a*b* data."), "ExtraSamples: alpha with this sample layout");
        }

        switch (Photometric)
        {
            case TiffFormat.PhotometricPalette:
                ResolveColorMap();
                break;

            case TiffFormat.PhotometricCmyk:
                ResolveInks();
                break;

            case TiffFormat.PhotometricYCbCr:
                ResolveYCbCr();
                break;

            case TiffFormat.PhotometricCieLab or TiffFormat.PhotometricIccLab:
                ResolveWhitePoint();
                break;

            case TiffFormat.PhotometricItuLab:
                ResolveWhitePoint();
                ResolveItuLabRanges(colorSamples);
                break;
        }
    }

    /// <summary>
    /// Resolves the samples of a DNG raw image (DNG specification 1.7.1, chapter 3): a color filter array of one sample per
    /// pixel, or linear raw data of one to four samples per pixel; unsigned integers of 8 to 16 bits, or floating-point
    /// numbers of 16, 24 or 32 bits.
    /// </summary>
    private void ResolveRawSamples()
    {
        if (!Directory.TryGetUnsigned(TiffFormat.PhotometricInterpretationTag, out var photometric))
            throw DngRawInvalid("has no PhotometricInterpretation tag, which is required");

        if (photometric is not (TiffFormat.PhotometricCfa or TiffFormat.PhotometricLinearRaw))
            throw DngRawUnsupported(string.Create(CultureInfo.InvariantCulture, $"The raw image (IFD {Index}) of the DNG file uses the {TiffFormat.GetPhotometricName((long)photometric)} photometric interpretation; CFA and LinearRaw raw data are supported."), string.Create(CultureInfo.InvariantCulture, $"PhotometricInterpretation: {photometric} in a raw IFD"));

        Photometric = (int)photometric;
        var samplesPerPixel = Directory.TryGetUnsigned(TiffFormat.SamplesPerPixelTag, out var samples) ? samples : 1;
        if (Photometric == TiffFormat.PhotometricCfa && samplesPerPixel != 1)
            throw DngRawUnsupported(string.Create(CultureInfo.InvariantCulture, $"The raw image (IFD {Index}) of the DNG file stores {samplesPerPixel} samples per pixel of a color filter array; a color filter array of one sample per pixel is supported."), "SamplesPerPixel: several samples of a color filter array");

        if (samplesPerPixel is 0 or > 4)
            throw DngRawUnsupported(string.Create(CultureInfo.InvariantCulture, $"The raw image (IFD {Index}) of the DNG file stores {samplesPerPixel} samples per pixel; linear raw data of 1 to 4 samples are supported."), string.Create(CultureInfo.InvariantCulture, $"SamplesPerPixel: {samplesPerPixel} in a raw IFD"));

        SamplesPerPixel = (int)samplesPerPixel;
        var bits = TryReadPerSampleValues(TiffFormat.BitsPerSampleTag) ?? [.. Enumerable.Repeat(1UL, SamplesPerPixel)];
        if (_isLenient && bits.Length == 1 && SamplesPerPixel > 1)
        {
            bits = [.. Enumerable.Repeat(bits[0], SamplesPerPixel)];
        }

        if (bits.Length != SamplesPerPixel)
            throw DngRawInvalid(string.Create(CultureInfo.InvariantCulture, $"declares {bits.Length} BitsPerSample values for {SamplesPerPixel} samples per pixel"));

        // DNG requires the same depth for every sample (chapter 3, BitsPerSample)
        foreach (var value in bits)
        {
            if (value != bits[0])
                throw DngRawInvalid(string.Create(CultureInfo.InvariantCulture, $"mixes sample widths ({string.Join(", ", bits)} bits), which DNG does not allow"));
        }

        SampleFormat = ResolveSampleFormat();
        switch (SampleFormat)
        {
            case TiffFormat.SampleFormatFloat when bits[0] is not (16 or 24 or 32):
                throw DngRawInvalid(string.Create(CultureInfo.InvariantCulture, $"stores {bits[0]}-bit floating-point samples; DNG allows 16, 24 and 32 bits"));

            case TiffFormat.SampleFormatSignedInteger:
                throw DngRawUnsupported(string.Create(CultureInfo.InvariantCulture, $"The raw image (IFD {Index}) of the DNG file stores signed integer samples; DNG stores unsigned integer and floating-point samples."), "SampleFormat: signed integers in a raw IFD");

            case TiffFormat.SampleFormatUnsignedInteger when bits[0] is < 8 or > 16:
                throw DngRawUnsupported(string.Create(CultureInfo.InvariantCulture, $"The raw image (IFD {Index}) of the DNG file stores {bits[0]}-bit integer samples; samples of 8 to 16 bits are supported."), string.Create(CultureInfo.InvariantCulture, $"BitsPerSample: {bits[0]} in a raw IFD"));
        }

        BitsPerSample = (int)bits[0];
    }

    /// <summary>
    /// Resolves how the blocks of a DNG raw image are stored (DNG specification 1.7.1, chapter 3, Compression and Predictor):
    /// uncompressed, lossless JPEG (7), Deflate (8, with the predictors of TIFF and DNG) or lossy JPEG (34892, 8-bit linear
    /// raw data); one plane.
    /// </summary>
    private void ResolveRawCompression()
    {
        var compression = Directory.TryGetUnsigned(TiffFormat.CompressionTag, out var value) ? value : TiffFormat.CompressionNone;
        if (compression is not (TiffFormat.CompressionNone or TiffFormat.CompressionJpeg or TiffFormat.CompressionAdobeDeflate or TiffFormat.CompressionDeflate or TiffFormat.CompressionDngLossyJpeg or TiffFormat.CompressionJpegXl))
            throw DngRawUnsupported(string.Create(CultureInfo.InvariantCulture, $"The raw image (IFD {Index}) of the DNG file uses {TiffFormat.GetCompressionName((long)compression)} compression; uncompressed, lossless JPEG, Deflate, lossy JPEG and JPEG XL raw data are supported."), string.Create(CultureInfo.InvariantCulture, $"Compression: {compression} in a raw IFD"));

        // JPEG XL codes one or three planes of integers of 8 to 16 bits, or of 16-bit floating-point numbers (chapter 3)
        if (compression == TiffFormat.CompressionJpegXl && (SamplesPerPixel is not (1 or 3) || (IsFloatingPoint && BitsPerSample != 16)))
            throw DngRawInvalid(string.Create(CultureInfo.InvariantCulture, $"uses JPEG XL compression with {SamplesPerPixel} samples of {BitsPerSample} bits; DNG allows one or three planes of integers or of 16-bit floating-point numbers"));

        Compression = (int)compression;
        var planar = Directory.TryGetUnsigned(TiffFormat.PlanarConfigurationTag, out var planarValue) ? planarValue : TiffFormat.PlanarChunky;
        if (planar is not (TiffFormat.PlanarChunky or TiffFormat.PlanarSeparate))
            throw DngRawInvalid(string.Create(CultureInfo.InvariantCulture, $"uses the planar configuration {planar}"));

        if (planar == TiffFormat.PlanarSeparate && SamplesPerPixel > 1)
            throw DngRawUnsupported(string.Create(CultureInfo.InvariantCulture, $"The raw image (IFD {Index}) of the DNG file stores its {SamplesPerPixel} samples in separate planes; chunky samples are supported."), "PlanarConfiguration: 2 in a raw IFD");

        // Packed samples are stored most significant bit first, even in a little-endian file (chapter 3, BitsPerSample)
        var fillOrder = Directory.TryGetUnsigned(TiffFormat.FillOrderTag, out var fillOrderValue) ? fillOrderValue : TiffFormat.FillOrderMostSignificantBitFirst;
        if (fillOrder != TiffFormat.FillOrderMostSignificantBitFirst)
            throw DngRawInvalid(string.Create(CultureInfo.InvariantCulture, $"uses the fill order {fillOrder}; DNG requires the default fill order"));

        if (IsFloatingPoint && (IsJpeg || Compression is TiffFormat.CompressionDngLossyJpeg))
            throw DngRawInvalid(string.Create(CultureInfo.InvariantCulture, $"uses {TiffFormat.GetCompressionName(Compression)} compression with floating-point samples, which DNG compresses with Deflate"));

        if (Compression == TiffFormat.CompressionDngLossyJpeg && (BitsPerSample != 8 || Photometric != TiffFormat.PhotometricLinearRaw))
            throw DngRawInvalid(string.Create(CultureInfo.InvariantCulture, $"uses lossy JPEG compression with {BitsPerSample}-bit {TiffFormat.GetPhotometricName(Photometric)} data; DNG allows it for 8-bit LinearRaw data"));

        var predictor = Directory.TryGetUnsigned(TiffFormat.PredictorTag, out var predictorValue) ? predictorValue : TiffFormat.PredictorNone;
        PredictorDistance = predictor switch
        {
            TiffFormat.PredictorNone or TiffFormat.PredictorHorizontalDifferencing or TiffFormat.PredictorFloatingPoint => 1,
            TiffFormat.PredictorHorizontalDifferencingX2 or TiffFormat.PredictorFloatingPointX2 => 2,
            TiffFormat.PredictorHorizontalDifferencingX4 or TiffFormat.PredictorFloatingPointX4 => 4,
            _ => throw DngRawUnsupported(string.Create(CultureInfo.InvariantCulture, $"The raw image (IFD {Index}) of the DNG file uses predictor {predictor}; no predictor, horizontal differencing and the floating-point predictor (with the X2 and X4 variants of DNG) are supported."), string.Create(CultureInfo.InvariantCulture, $"Predictor: {predictor} in a raw IFD")),
        };

        Predictor = (int)predictor;

        // DNG allows the predictors with Deflate compression only; the lossless JPEG datastream has its own
        if (Predictor != TiffFormat.PredictorNone && !IsDeflate)
            throw DngRawInvalid(string.Create(CultureInfo.InvariantCulture, $"uses predictor {Predictor} with {TiffFormat.GetCompressionName(Compression)} compression; DNG allows predictors with Deflate compression only"));

        if (HasHorizontalDifferencing && (IsFloatingPoint || BitsPerSample is not (8 or 16)))
            throw DngRawUnsupported(string.Create(CultureInfo.InvariantCulture, $"The raw image (IFD {Index}) of the DNG file uses horizontal differencing with {BitsPerSample}-bit {GetSampleFormatName()} samples; it is supported with 8- and 16-bit integer samples."), "Predictor: horizontal differencing with this sample layout");

        if (HasFloatingPointPredictor && !IsFloatingPoint)
            throw DngRawInvalid(string.Create(CultureInfo.InvariantCulture, $"uses the floating-point predictor {Predictor} with integer samples"));

        if (IsJpeg && Directory.Find(TiffFormat.JpegTablesTag) is { } field)
        {
            var length = TiffDirectory.GetValueLength(field);
            if (field.Type is not (TiffFieldType.Undefined or TiffFieldType.Byte) || length > TiffFormat.MaxJpegTablesLength)
                throw DngRawInvalid(string.Create(CultureInfo.InvariantCulture, $"declares a JPEGTables tag of {field.Count} values of {TiffFieldTypes.GetName(field.Type)}"));

            using var buffer = Directory.ReadValues(field);
            JpegTables = buffer.Span.ToArray();
        }
    }

    private InvalidImageContentException DngRawInvalid(string problem)
        => new(string.Create(CultureInfo.InvariantCulture, $"The raw image (IFD {Index}) of the DNG file {problem}."), ImageFormat.Dng);

    private static UnsupportedImageFeatureException DngRawUnsupported(string message, string feature) => new(message, ImageFormat.Dng, feature);

    /// <summary>Reads the format of the samples, which must be the same for every sample of a pixel.</summary>
    private int ResolveSampleFormat()
    {
        // Some writers store one value for all the samples: the values that are present must agree
        var formats = TryReadPerSampleValues(TiffFormat.SampleFormatTag);
        if (formats is null or [])
            return TiffFormat.SampleFormatUnsignedInteger;

        var result = 0UL;
        foreach (var value in formats)
        {
            // An undefined format is the default: unsigned integer data
            var format = value == TiffFormat.SampleFormatUndefined ? TiffFormat.SampleFormatUnsignedInteger : value;
            if (format is not (TiffFormat.SampleFormatUnsignedInteger or TiffFormat.SampleFormatSignedInteger or TiffFormat.SampleFormatFloat))
                throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} stores samples of the SampleFormat {value}; unsigned integer, signed integer and floating-point samples are supported."), string.Create(CultureInfo.InvariantCulture, $"SampleFormat: {value}"));

            if (result != 0 && format != result)
                throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} mixes sample formats ({string.Join(", ", formats)}); every sample must have the same format."), "SampleFormat: mixed formats");

            result = format;
        }

        return (int)result;
    }

    /// <summary>
    /// Reads a tag that has a value per sample. Leniently, the values past the number of samples are ignored, as libtiff,
    /// Pillow and Apple ImageIO do; strictly, they are an error.
    /// </summary>
    private ulong[]? TryReadPerSampleValues(ushort tag)
    {
        if (_isLenient && Directory.Find(tag) is { } field && field.Count > (ulong)SamplesPerPixel)
            return Directory.ReadLeadingUnsignedValues(field, SamplesPerPixel);

        return Directory.TryReadUnsignedArray(tag, SamplesPerPixel);
    }

    private string GetSampleFormatName() => SampleFormat switch
    {
        TiffFormat.SampleFormatSignedInteger => "signed integer",
        TiffFormat.SampleFormatFloat => "floating-point",
        _ => "unsigned integer",
    };

    private UnsupportedImageFeatureException UnsupportedBitsPerSample(ulong bits, string supported)
        => TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} stores {bits}-bit samples; {supported}."), string.Create(CultureInfo.InvariantCulture, $"BitsPerSample: {bits}"));

    private void ResolveColorMap()
    {
        if (Directory.Find(TiffFormat.ColorMapTag) is not { } field)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} stores palette indexes without a ColorMap tag."));

        // Three values per index, however many indexes the pixels use: all the red values, then the green and blue ones
        var values = Directory.ReadUnsignedArray(field, 3L << BitsPerSample);
        var map = new ushort[values.Length];
        var isMultipleOf257 = true;
        var isMultipleOf256 = true;
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] > ushort.MaxValue)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The ColorMap of the TIFF page {Index} stores the value {values[i]}; the largest one is 65,535."));

            map[i] = (ushort)values[i];
            isMultipleOf257 &= values[i] % 257 == 0;
            isMultipleOf256 &= values[i] % 256 == 0;
        }

        ColorMap = map;
        HasSixteenBitColorMap = !isMultipleOf257 && !isMultipleOf256;
        ColorMapDivisor = isMultipleOf257 ? 257 : 256;
    }

    private void ResolveInks()
    {
        var inkSet = Directory.TryGetUnsigned(TiffFormat.InkSetTag, out var inkSetValue) ? inkSetValue : TiffFormat.InkSetCmyk;
        if (inkSet != TiffFormat.InkSetCmyk)
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} stores separations that are not cyan, magenta, yellow and black (InkSet {inkSet})."), string.Create(CultureInfo.InvariantCulture, $"InkSet: {inkSet}"));

        if (Directory.TryGetUnsigned(TiffFormat.NumberOfInksTag, out var inkCount) && inkCount != 4)
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} stores {inkCount} inks; cyan, magenta, yellow and black are supported."), string.Create(CultureInfo.InvariantCulture, $"NumberOfInks: {inkCount}"));

        // DotRange gives the samples of 0% and 100% dot; the conversion of the inks reads the whole range of the samples
        var dotRange = Directory.TryReadUnsignedArray(TiffFormat.DotRangeTag, 8) ?? [];
        var max = (1UL << BitsPerSample) - 1;
        for (var i = 0; i < dotRange.Length; i++)
        {
            if (dotRange[i] != (i % 2 == 0 ? 0 : max))
                throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} declares a DotRange other than 0 to {max}."), "DotRange");
        }
    }

    private void ResolveWhitePoint()
    {
        if (Directory.TryReadRationalArray(TiffFormat.WhitePointTag, 2) is not { } chromaticity)
            return;

        // A chromaticity has positive x, y and 1 - x - y
        if (!(chromaticity[0] > 0 && chromaticity[1] > 0 && chromaticity[0] + chromaticity[1] < 1))
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} declares the white point ({chromaticity[0]}, {chromaticity[1]}), which is not a chromaticity."));

        WhitePoint = (chromaticity[0], chromaticity[1]);
    }

    /// <summary>
    /// Resolves the ranges of the components of an <c>ITULab</c> page: the <c>Decode</c> tag gives the minimum and the
    /// maximum of each sample (two values per sample, those of the alpha sample are not read), or the defaults of RFC 2301.
    /// </summary>
    private void ResolveItuLabRanges(int colorSamples)
    {
        double[] defaults = [0, 100, -85, 85, -75, 125];
        var ranges = Directory.TryReadSignedRationalArray(TiffFormat.DecodeTag, colorSamples * 2) ?? defaults[..(colorSamples * 2)];
        foreach (var value in ranges)
        {
            // A range runs from the value of the sample 0 to the one of the largest sample; a decreasing one is valid
            if (!double.IsFinite(value) || Math.Abs(value) > 1000)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} declares the Decode values ({string.Join(", ", ranges)}); each must be a component of an L*a*b* color, between -1000 and 1000."));
        }

        ItuLabRanges = ranges;
    }

    private void ResolveYCbCr()
    {
        var subsampling = Directory.TryReadUnsignedArray(TiffFormat.YCbCrSubSamplingTag, 2) ?? [2, 2];
        if (subsampling.Length != 2 || subsampling[0] is not (1 or 2 or 4) || subsampling[1] is not (1 or 2 or 4))
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} declares the YCbCrSubSampling values ({string.Join(", ", subsampling)}); two values among 1, 2 and 4 are expected."));

        var coefficients = Directory.TryReadRationalArray(TiffFormat.YCbCrCoefficientsTag, 3);
        var reference = Directory.TryReadRationalArray(TiffFormat.ReferenceBlackWhiteTag, 6);
        YCbCr = TiffYCbCrConverter.Create(coefficients, reference, (int)subsampling[0], (int)subsampling[1], BitsPerSample)
            ?? throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} declares YCbCrCoefficients or ReferenceBlackWhite values that define no conversion to RGB."));
    }

    private void ResolveCompression()
    {
        var compression = Directory.TryGetUnsigned(TiffFormat.CompressionTag, out var value) ? value : TiffFormat.CompressionNone;
        if (compression is not (TiffFormat.CompressionNone or TiffFormat.CompressionCcittRle or TiffFormat.CompressionCcittFax3 or TiffFormat.CompressionCcittFax4 or TiffFormat.CompressionLzw
            or TiffFormat.CompressionJpeg or TiffFormat.CompressionOldJpeg or TiffFormat.CompressionAdobeDeflate or TiffFormat.CompressionPackBits or TiffFormat.CompressionDeflate or TiffFormat.CompressionJpeg2000))
        {
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses {TiffFormat.GetCompressionName((long)compression)} compression; uncompressed, Deflate, LZW, PackBits, CCITT (modified Huffman, Group 3 and Group 4), JPEG (and old-style JPEG) and JPEG 2000 data are supported."),string.Create(CultureInfo.InvariantCulture, $"Compression: {compression}"));
        }

        Compression = (int)compression;

        // With one sample per pixel there is one plane, however it is called
        var planar = Directory.TryGetUnsigned(TiffFormat.PlanarConfigurationTag, out var planarValue) ? planarValue : TiffFormat.PlanarChunky;
        if (planar is not (TiffFormat.PlanarChunky or TiffFormat.PlanarSeparate))
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses the planar configuration {planar}; chunky (1) and separate (2) planes are supported."), string.Create(CultureInfo.InvariantCulture, $"PlanarConfiguration: {planar}"));

        IsPlanar = planar == TiffFormat.PlanarSeparate && SamplesPerPixel > 1;
        PlaneYCbCr = IsPlanar ? YCbCr?.WithoutSubsampling() : null;

        // The planes of YCbCr data are read uncompressed or with a lossless compression. The chroma planes of subsampled
        // data have fewer samples (GetPlaneSubsampling); a JPEG or JPEG 2000 datastream would hold each plane as an image
        if (IsPlanar && IsYCbCr && Compression is TiffFormat.CompressionJpeg or TiffFormat.CompressionOldJpeg or TiffFormat.CompressionJpeg2000)
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} stores JPEG-compressed YCbCr data in separate planes; YCbCr planes are supported uncompressed and with the lossless compressions, and JPEG-compressed YCbCr data with chunky samples."), "PlanarConfiguration: 2 with YCbCr data");

        // The reversed order is the order of fax devices, which TIFF 6.0 recommends for bilevel data only. It reverses the bits
        // of every stored byte, whatever the width of the samples, before the decompression (libtiff does the same); a JPEG
        // or JPEG 2000 datastream has its own bit order, which libtiff never reverses, so the order is not defined there
        var fillOrder = Directory.TryGetUnsigned(TiffFormat.FillOrderTag, out var fillOrderValue) ? fillOrderValue : TiffFormat.FillOrderMostSignificantBitFirst;
        if (fillOrder is not (TiffFormat.FillOrderMostSignificantBitFirst or TiffFormat.FillOrderLeastSignificantBitFirst))
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses the fill order {fillOrder}; 1 (most significant bit first) and 2 (least significant bit first) are supported."), string.Create(CultureInfo.InvariantCulture, $"FillOrder: {fillOrder}"));

        if (fillOrder == TiffFormat.FillOrderLeastSignificantBitFirst && Compression is TiffFormat.CompressionJpeg or TiffFormat.CompressionOldJpeg or TiffFormat.CompressionJpeg2000)
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses the fill order 2 with {TiffFormat.GetCompressionName(Compression)} compression, whose datastream has its own bit order."), string.Create(CultureInfo.InvariantCulture, $"FillOrder: 2 with {TiffFormat.GetCompressionName(Compression)}"));

        IsLeastSignificantBitFirst = fillOrder == TiffFormat.FillOrderLeastSignificantBitFirst;

        // The JPEG and JPEG 2000 datastreams of a page hold its samples: they code unsigned integers here
        if (SampleFormat != TiffFormat.SampleFormatUnsignedInteger && (IsJpeg || IsJpeg2000))
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses {TiffFormat.GetCompressionName(Compression)} compression with {GetSampleFormatName()} samples; they are supported uncompressed and with the Deflate, LZW and PackBits compressions."), string.Create(CultureInfo.InvariantCulture, $"Compression: {TiffFormat.GetCompressionName(Compression)} with SampleFormat {SampleFormat}"));

        var predictor = Directory.TryGetUnsigned(TiffFormat.PredictorTag, out var predictorValue) ? predictorValue : TiffFormat.PredictorNone;
        if (predictor is not (TiffFormat.PredictorNone or TiffFormat.PredictorHorizontalDifferencing or TiffFormat.PredictorFloatingPoint))
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses predictor {predictor}; no predictor (1), horizontal differencing (2) and the floating-point predictor (3) are supported."), string.Create(CultureInfo.InvariantCulture, $"Predictor: {predictor}"));

        Predictor = (int)predictor;
        if (Predictor == TiffFormat.PredictorHorizontalDifferencing && (BitsPerSample < 8 || IsYCbCr || IsCcitt || IsJpeg || IsJpeg2000 || IsFloatingPoint))
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses horizontal differencing with {BitsPerSample}-bit {GetSampleFormatName()} {TiffFormat.GetPhotometricName(Photometric)} data and {TiffFormat.GetCompressionName(Compression)} compression; it is supported with 8- and 16-bit integer samples that are not YCbCr, and not with the CCITT, JPEG and JPEG 2000 compressions (floating-point samples use the floating-point predictor)."), "Predictor: 2 with this sample layout or compression");

        // TIFF Technical Note 3 defines the floating-point predictor for floating-point samples only
        if (Predictor == TiffFormat.PredictorFloatingPoint && !IsFloatingPoint)
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses the floating-point predictor with {GetSampleFormatName()} samples; it is supported with floating-point samples."), "Predictor: 3 with integer samples");

        if (IsCcitt)
        {
            ResolveFaxOptions();
        }
        else if (IsJpeg)
        {
            ResolveJpeg();
        }
        else if (IsJpeg2000)
        {
            ResolveJpeg2000();
        }
    }

    private void ResolveFaxOptions()
    {
        if (BitsPerPixel != 1 || !IsGrayscale)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses {TiffFormat.GetCompressionName(Compression)} compression, which codes bilevel data, with {SamplesPerPixel} sample(s) of {BitsPerSample} bits and the {TiffFormat.GetPhotometricName(Photometric)} photometric interpretation."));

        if (Compression == TiffFormat.CompressionCcittFax3)
        {
            var options = Directory.TryGetUnsigned(TiffFormat.T4OptionsTag, out var value) ? value : 0;
            if ((options & TiffFormat.T4OptionUncompressedMode) != 0)
                throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses the uncompressed mode of Group 3 coding."), "T4Options: uncompressed mode");

            IsTwoDimensionalFax = (options & TiffFormat.T4OptionTwoDimensional) != 0;
        }
        else if (Compression == TiffFormat.CompressionCcittFax4)
        {
            var options = Directory.TryGetUnsigned(TiffFormat.T6OptionsTag, out var value) ? value : 0;
            if ((options & TiffFormat.T6OptionUncompressedMode) != 0)
                throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses the uncompressed mode of Group 4 coding."), "T6Options: uncompressed mode");
        }
    }

    private void ResolveJpeg2000()
    {
        // The codestream of a block holds the samples of the page as its components: its own color transformation is
        // undone by its decoder, so the page cannot declare another one
        if (BitsPerSample is not (8 or 16))
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses JPEG 2000 compression with {BitsPerSample}-bit samples; 8- and 16-bit samples are supported."), string.Create(CultureInfo.InvariantCulture, $"Compression: JPEG 2000 with {BitsPerSample}-bit samples"));

        if (IsPalette || IsYCbCr || IsCieLab)
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses JPEG 2000 compression with {(IsPalette ? "palette indexes" : TiffFormat.GetPhotometricName(Photometric) + " data")}; grayscale, RGB and CMYK data are supported."), "Compression: JPEG 2000 with this sample layout");
    }

    private void ResolveJpeg()
    {
        // The datastream of every block must have this precision: 8 or 12 bits, or any of them when it is lossless
        if (BitsPerSample < 8)
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses JPEG compression with {BitsPerSample}-bit samples; samples of 8 to 16 bits are supported."), string.Create(CultureInfo.InvariantCulture, $"Compression: JPEG with {BitsPerSample}-bit samples"));

        // TIFF-FX (RFC 2301) codes ITU L*a*b* data with JPEG: the components of the datastream are the samples, without color transformation
        var isJpegLab = Photometric == TiffFormat.PhotometricItuLab && Compression == TiffFormat.CompressionJpeg;
        if (IsPalette || HasAlpha || (IsCieLab && !isJpegLab))
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses {TiffFormat.GetCompressionName(Compression)} compression with {(IsPalette ? "palette indexes" : HasAlpha ? "an alpha channel" : TiffFormat.GetPhotometricName(Photometric) + " data")}; grayscale, RGB, YCbCr and CMYK data without extra sample are supported, and ITU L*a*b* data with JPEG (not old-style JPEG) compression."), "Compression: JPEG with this sample layout");

        if (IsOldJpeg)
        {
            // libtiff decodes these pages with the color conversion of the JPEG decoder, whatever ReferenceBlackWhite says
            var process = Directory.TryGetUnsigned(TiffFormat.JpegProcTag, out var processValue) ? processValue : 1;
            if (process != 1)
                throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The old-style JPEG page {Index} uses the JPEG process {process}; the baseline process (1) is supported."), string.Create(CultureInfo.InvariantCulture, $"Compression: old-style JPEG with JPEGProc {process}"));

            // The DCT-based processes code samples of 8 or 12 bits
            if (BitsPerSample is not (8 or 12))
                throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The old-style JPEG page {Index} stores {BitsPerSample}-bit samples; 8- and 12-bit samples are supported."), string.Create(CultureInfo.InvariantCulture, $"Compression: old-style JPEG with {BitsPerSample}-bit samples"));

            return;
        }

        if (YCbCr is { IsJfifCompatible: false })
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses JPEG compression with YCbCrCoefficients or ReferenceBlackWhite values other than the full-range ITU-R BT.601 ones of JFIF (black at 0, white at {(1 << BitsPerSample) - 1} and chroma centered on {1 << (BitsPerSample - 1)} for {BitsPerSample}-bit samples)."), "Compression: JPEG with other YCbCr coefficients");

        if (Directory.Find(TiffFormat.JpegTablesTag) is { } field)
        {
            var length = TiffDirectory.GetValueLength(field);
            if (field.Type is not (TiffFieldType.Undefined or TiffFieldType.Byte) || length > TiffFormat.MaxJpegTablesLength)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEGTables tag of the TIFF page {Index} declares {field.Count} values of {TiffFieldTypes.GetName(field.Type)}; at most {TiffFormat.MaxJpegTablesLength} bytes are expected."));

            using var buffer = Directory.ReadValues(field);
            JpegTables = buffer.Span.ToArray();
        }
    }

    private void ResolveBlocks(ImageCodecContext context)
    {
        var hasTileOffsets = Directory.Find(TiffFormat.TileOffsetsTag) is not null;
        var hasStripOffsets = Directory.Find(TiffFormat.StripOffsetsTag) is not null;
        if (hasTileOffsets && hasStripOffsets)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} declares both StripOffsets and TileOffsets."));

        IsTiled = hasTileOffsets;
        var hasInterchangeFormat = IsOldJpeg && Directory.Find(TiffFormat.JpegInterchangeFormatTag) is not null;
        ushort offsetsTag, byteCountsTag;
        if (IsTiled)
        {
            var tileWidth = GetRequiredUnsigned(TiffFormat.TileWidthTag, "TileWidth");
            var tileLength = GetRequiredUnsigned(TiffFormat.TileLengthTag, "TileLength");
            if (tileWidth == 0 || tileLength == 0 || tileWidth > int.MaxValue || tileLength > int.MaxValue)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} declares {tileWidth}x{tileLength} tiles; both dimensions must be positive and representable."));

            // DNG writers (the DNG SDK among them) size tiles to the image, whatever TIFF requires
            if (!IsDngRaw && (tileWidth % 16 != 0 || tileLength % 16 != 0))
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} declares {tileWidth}x{tileLength} tiles; TIFF requires both to be multiples of 16."));

            BlockWidth = (int)tileWidth;
            BlockHeight = (int)tileLength;
            BlocksAcross = DivideRoundingUp(Size.Width, BlockWidth);
            BlocksDown = DivideRoundingUp(Size.Height, BlockHeight);
            offsetsTag = TiffFormat.TileOffsetsTag;
            byteCountsTag = TiffFormat.TileByteCountsTag;
        }
        else
        {
            // An old-style JPEG page may keep its whole datastream in its JPEG interchange format
            if (!hasStripOffsets && !hasInterchangeFormat)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} has neither StripOffsets nor TileOffsets, so it stores no pixel data."));

            var rowsPerStrip = Directory.TryGetUnsigned(TiffFormat.RowsPerStripTag, out var value) ? value : uint.MaxValue;
            if (rowsPerStrip == 0)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} declares RowsPerStrip 0."));

            BlockWidth = Size.Width;
            BlockHeight = (int)Math.Min(rowsPerStrip, (ulong)Size.Height);
            BlocksAcross = 1;
            BlocksDown = DivideRoundingUp(Size.Height, BlockHeight);
            offsetsTag = TiffFormat.StripOffsetsTag;
            byteCountsTag = TiffFormat.StripByteCountsTag;

            // A strip stores whole data units: only the last strip of a page may end in the middle of one
            if (YCbCr is { } conversion && !IsJpeg && BlocksDown > 1 && BlockHeight % conversion.VerticalSubsampling != 0)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} declares strips of {BlockHeight} rows of YCbCr data whose chroma samples cover {conversion.VerticalSubsampling} rows."));
        }

        var blockLength = ResolveBlockRowLength(context);
        if (hasInterchangeFormat && (Directory.Find(offsetsTag) is null || Directory.Find(byteCountsTag) is null))
        {
            ResolveOldJpeg(context, hasBlocks: false);
            return;
        }

        // The blocks of the planes follow each other
        var blockCount = (long)BlocksAcross * BlocksDown * PlaneCount;
        var offsetsField = Directory.Find(offsetsTag)!.Value;
        BlockOffsets = Directory.ReadUnsignedArray(offsetsField, blockCount);

        // Leniently, the one uncompressed block of a page is as long as its geometry says when the byte counts are missing
        // or, for a strip, too small, as libtiff, Pillow and Apple ImageIO read it: these bytes are in the file, and a block
        // that runs past its end is still an error. Byte counts beyond the number of blocks are ignored, as they all do
        var hasGeometricLength = _isLenient && blockCount == 1 && Compression == TiffFormat.CompressionNone && BlockYCbCr is null;
        if (Directory.Find(byteCountsTag) is not { } byteCountsField)
        {
            if (!hasGeometricLength)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} declares {(IsTiled ? "TileOffsets" : "StripOffsets")} without the matching byte counts."));

            BlockByteCounts = [(ulong)blockLength];
            return;
        }

        BlockByteCounts = _isLenient && byteCountsField.Count > (ulong)blockCount
            ? Directory.ReadLeadingUnsignedValues(byteCountsField, (int)blockCount)
            : Directory.ReadUnsignedArray(byteCountsField, blockCount);

        if (hasGeometricLength && !IsTiled && BlockByteCounts[0] < (ulong)blockLength)
        {
            BlockByteCounts[0] = (ulong)blockLength;
        }

        if (IsOldJpeg)
        {
            ResolveOldJpeg(context, hasBlocks: true);
        }
    }

    /// <summary>Computes <see cref="BlockRowLength"/> from the geometry of the blocks, and returns the length of a block.</summary>
    private long ResolveBlockRowLength(ImageCodecContext context)
    {
        // A row is held as stored samples, as pixels of the working representation, and for 16-bit inks as five native
        // samples per pixel on the way to their conversion. A block of a planar page holds one sample per pixel
        var blockRowLength = (((long)BlockWidth * BlockSamplesPerPixel * DecodedBitsPerSample) + 7) / 8;
        var expandedRowLength = (long)Size.Width * Math.Max(PixelFormats.GetBytesPerPixel(SourcePixelFormat), IsCmyk ? 5 * sizeof(ushort) : 0);
        if (blockRowLength > CheckedSizes.MaxBufferLength || RowLength > CheckedSizes.MaxBufferLength || expandedRowLength > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(context.Limits);

        BlockRowLength = (int)blockRowLength;
        if (!CheckedSizes.TryMultiply(blockRowLength, BlockHeight, out var blockLength) || blockLength > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(context.Limits);

        return blockLength;
    }

    /// <summary>
    /// Resolves how the blocks of an old-style JPEG page are decoded (<see cref="TiffOldJpegLayout"/>). A page decoded as a
    /// single datastream gets a single block, as tall as the page.
    /// </summary>
    private void ResolveOldJpeg(ImageCodecContext context, bool hasBlocks)
    {
        var source = Directory.Source;
        if (Directory.TryGetUnsigned(TiffFormat.JpegInterchangeFormatTag, out var offset))
        {
            ulong? length = Directory.TryGetUnsigned(TiffFormat.JpegInterchangeFormatLengthTag, out var lengthValue) ? lengthValue : null;
            if (offset >= (ulong)source.Length)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The old-style JPEG page {Index} declares its JPEG interchange format at offset {offset}, outside the input."));

            // The header is read in growing pieces, so that the scan that may follow it is not read
            var available = (ulong)source.Length - offset;
            var limit = Math.Min(length ?? available, available);
            foreach (var piece in (ReadOnlySpan<long>)[4096, TiffFormat.MaxJpegTablesLength, TiffFormat.MaxOldJpegHeaderLength])
            {
                if (TryReadOldJpegHeader(offset, (long)Math.Min((ulong)piece, limit), length, hasBlocks, isLast: (ulong)piece >= limit || piece == TiffFormat.MaxOldJpegHeaderLength))
                    break;
            }
        }
        else
        {
            OldJpeg = ReadOldJpegTables();
        }

        if (OldJpeg!.IsWholeImage)
        {
            // The blocks are the slices of one scan, whose frame covers the page
            IsTiled = false;
            IsPlanar = false;
            BlockWidth = Size.Width;
            BlockHeight = Size.Height;
            BlocksAcross = 1;
            BlocksDown = 1;
            _ = ResolveBlockRowLength(context);
            var total = 0UL;
            foreach (var (_, rangeLength) in OldJpeg.DataRanges)
            {
                total += rangeLength;
            }

            BlockOffsets = [OldJpeg.DataRanges[0].Offset];
            BlockByteCounts = [total];
        }
    }

    /// <summary>Reads the header of the JPEG interchange format of an old-style JPEG page from its first <paramref name="readLength"/> bytes.</summary>
    /// <returns><see langword="false"/> when the bytes end before the header, and more of them can be read.</returns>
    private bool TryReadOldJpegHeader(ulong offset, long readLength, ulong? length, bool hasBlocks, bool isLast)
    {
        using var buffer = Directory.Source.ReadToBuffer((long)offset, readLength);

        // A malformed header is reported by the parse of the layout
        var headerLength = TiffOldJpegLayout.FindHeaderLength(buffer.Span);
        if (headerLength == 0)
        {
            if (isLast)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The JPEG interchange format of the old-style JPEG page {Index} has no start-of-scan marker in its first {buffer.Length} bytes."));

            return false;
        }

        OldJpeg = TiffOldJpegLayout.FromInterchangeFormat(this, headerLength > 0 ? buffer.Span[..headerLength] : buffer.Span, offset, length, hasBlocks);
        return true;
    }

    /// <summary>Reads the tables of an old-style JPEG page that has no JPEG interchange format, from its tags.</summary>
    private TiffOldJpegLayout ReadOldJpegTables()
    {
        var count = SamplesPerPixel;
        var quantizationOffsets = Directory.TryReadUnsignedArray(TiffFormat.JpegQTablesTag, count);
        var dcOffsets = Directory.TryReadUnsignedArray(TiffFormat.JpegDcTablesTag, count);
        var acOffsets = Directory.TryReadUnsignedArray(TiffFormat.JpegAcTablesTag, count);
        if (quantizationOffsets?.Length != count || dcOffsets?.Length != count || acOffsets?.Length != count)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The old-style JPEG page {Index} has neither a JPEG interchange format nor the JPEGQTables, JPEGDCTables and JPEGACTables tags of its {count} component(s)."));

        var quantization = new byte[count][];
        var dc = new byte[count][];
        var ac = new byte[count][];
        var offsets = new (ulong, ulong, ulong)[count];
        for (var i = 0; i < count; i++)
        {
            quantization[i] = ReadOldJpegBytes(quantizationOffsets[i], 64);
            dc[i] = ReadHuffmanTable(dcOffsets[i]);
            ac[i] = ReadHuffmanTable(acOffsets[i]);
            offsets[i] = (quantizationOffsets[i], dcOffsets[i], acOffsets[i]);
        }

        var restartInterval = Directory.TryGetUnsigned(TiffFormat.JpegRestartIntervalTag, out var interval) ? interval : 0;
        if (restartInterval > ushort.MaxValue)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The old-style JPEG page {Index} declares the restart interval {restartInterval}; the largest one is 65,535."));

        return TiffOldJpegLayout.FromTables(this, quantization, dc, ac, offsets, (int)restartInterval);

        byte[] ReadHuffmanTable(ulong offset)
        {
            // The 16 numbers of codes of each length, then the values of the codes
            var counts = ReadOldJpegBytes(offset, 16);
            var valueCount = counts.Sum(static value => value);
            if (valueCount is 0 or > 256)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"An old-style JPEG Huffman table of the TIFF page {Index} declares {valueCount} codes."));

            return ReadOldJpegBytes(offset, 16 + valueCount);
        }
    }

    private byte[] ReadOldJpegBytes(ulong offset, int length)
    {
        var source = Directory.Source;
        if (offset > (ulong)source.Length || (ulong)source.Length - offset < (ulong)length)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"An old-style JPEG table of the TIFF page {Index} is outside the input."));

        using var buffer = source.ReadToBuffer((long)offset, length);
        return buffer.Span.ToArray();
    }

    private void ResolveMetadata(ImageCodecContext context)
    {
        var builder = new DecodedMetadataBuilder(context.Tracker, ImageFormat.Tiff);
        builder.TrySetResolution(ReadResolution());
        if (Directory.TryGetUnsigned(TiffFormat.OrientationTag, out var orientation) && orientation is >= 1 and <= 8)
        {
            // Orientation is metadata, exactly like the EXIF tag: it is reported and never applied to the pixels
            builder.Metadata.Orientation = (ExifOrientation)orientation;
        }

        // The pixels of an L*a*b* page are converted from the colorimetry it declares: a profile it embeds does not describe them once loaded
        if (!IsCieLab && Directory.TryReadBytes(TiffFormat.IccProfileTag, context.Limits.MaxMetadataBytes) is { } icc)
        {
            // A profile describes the stored samples: a CMYK one for inks, which the conversion of the page then reads
            builder.Charge(icc.Length);
            builder.TryAdoptIccProfile(icc, IsGrayscale ? IccProfileColorSpace.Gray : IsCmyk ? IccProfileColorSpace.Cmyk : IccProfileColorSpace.Rgb);
        }

        if (Directory.TryReadBytes(TiffFormat.XmpTag, context.Limits.MaxMetadataBytes) is { } xmp)
        {
            builder.TryAdoptXmp(xmp);
        }

        // Floating-point samples hold linear light (1 is the reference white), unless a profile says how they are encoded
        if (IsFloatingPoint && builder.Metadata.IccProfile is null)
        {
            builder.Metadata.TransferFunction = ColorTransferFunction.Linear;
        }

        Metadata = builder.Metadata;
    }

    private ImageResolution? ReadResolution()
    {
        var unit = Directory.TryGetUnsigned(TiffFormat.ResolutionUnitTag, out var value) ? value : TiffFormat.ResolutionUnitInch;
        if (unit is not (TiffFormat.ResolutionUnitInch or TiffFormat.ResolutionUnitCentimeter))
            return null;

        var x = Directory.TryReadPositiveRational(TiffFormat.XResolutionTag);
        var y = Directory.TryReadPositiveRational(TiffFormat.YResolutionTag);
        if (x is null || y is null)
            return null;

        var factor = unit == TiffFormat.ResolutionUnitCentimeter ? ResolutionConversion.CentimetersPerInch : 1;
        return new ImageResolution(x.Value * factor, y.Value * factor);
    }

    private ulong GetRequiredUnsigned(ushort tag, string name)
        => Directory.TryGetUnsigned(tag, out var value)
            ? value
            : throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} has no {name} tag, which is required."));

    private static int DivideRoundingUp(int value, int divisor) => ((value - 1) / divisor) + 1;
}
