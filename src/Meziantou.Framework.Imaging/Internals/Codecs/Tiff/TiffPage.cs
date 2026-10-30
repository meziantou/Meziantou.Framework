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
/// and YCbCr (8 bits) data; signed integer grayscale and RGB samples of 8 or 16 bits; floating-point <c>BlackIsZero</c>
/// grayscale and RGB samples of 16 (half precision), 24 or 32 bits; chunky planar configuration; strips or tiles; no
/// compression, Deflate, LZW, PackBits, the CCITT facsimile compressions, JPEG and JPEG 2000; the horizontal differencing
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
/// CMYK samples are inks: no pixel format holds them, so the page is described here like any other (an identification
/// needs nothing else) and its pixels are only decoded with a color conversion (<see cref="TiffPageDecoder"/>).
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
    /// to 15 bits of a JPEG datastream, which its decoder scales to 16 bits.
    /// </summary>
    public int DecodedBitsPerSample => BitsPerSample is > 8 and < 16 ? 16 : BitsPerSample;

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

    /// <summary>Gets the photometric interpretation (0, 1, 2, 3, 5 or 6).</summary>
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

    /// <summary>Gets the compression (1, 2, 3, 4, 5, 7, 8, 32773 or 32946).</summary>
    public int Compression { get; private set; }

    /// <summary>Gets a value indicating whether the strips or tiles are Deflate compressed.</summary>
    public bool IsDeflate => Compression is TiffFormat.CompressionAdobeDeflate or TiffFormat.CompressionDeflate;

    /// <summary>Gets a value indicating whether the strips or tiles use one of the CCITT facsimile compressions.</summary>
    public bool IsCcitt => Compression is TiffFormat.CompressionCcittRle or TiffFormat.CompressionCcittFax3 or TiffFormat.CompressionCcittFax4;

    /// <summary>Gets a value indicating whether each strip or tile is a JPEG datastream.</summary>
    public bool IsJpeg => Compression == TiffFormat.CompressionJpeg;

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

    /// <summary>Gets the file offset of each strip or tile, in reading order.</summary>
    public ulong[] BlockOffsets { get; private set; } = [];

    /// <summary>Gets the stored length of each strip or tile, in reading order.</summary>
    public ulong[] BlockByteCounts { get; private set; } = [];

    /// <summary>
    /// Gets the number of bytes of one full row of a decoded block, padded to a whole number of bytes. The rows of a
    /// YCbCr page are the RGB samples its data units convert to.
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
            if (IsPalette)
                return HasSixteenBitColorMap ? PixelFormat.Rgb48 : PixelFormat.Rgb24;

            if (IsFloatingPoint)
                return DefaultPixelFormats.ForTiffFloatingPoint(BitsPerSample);

            return DefaultPixelFormats.ForTiff(IsGrayscale, HasAlpha, DecodedBitsPerSample == 16);
        }
    }

    /// <summary>Gets the color model reported by identification.</summary>
    public ImageColorModel ColorModel => Photometric switch
    {
        TiffFormat.PhotometricPalette => ImageColorModel.Indexed,
        TiffFormat.PhotometricCmyk => ImageColorModel.Cmyk,
        TiffFormat.PhotometricYCbCr => ImageColorModel.YCbCr,
        TiffFormat.PhotometricRgb => HasAlpha ? ImageColorModel.Rgba : ImageColorModel.Rgb,
        _ => HasAlpha ? ImageColorModel.GrayscaleAlpha : ImageColorModel.Grayscale,
    };

    /// <summary>Resolves and validates one page.</summary>
    /// <param name="directory">The directory of the page.</param>
    /// <param name="index">The zero-based index of the page.</param>
    /// <param name="context">The operation context (limits and metadata accounting).</param>
    /// <returns>The page.</returns>
    /// <exception cref="InvalidImageContentException">A required tag is missing, or a tag is inconsistent with the rest of the page.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The page uses a recognized but unsupported variant.</exception>
    /// <exception cref="ImageResourceLimitException">The page exceeds a configured limit.</exception>
    public static TiffPage Resolve(TiffDirectory directory, int index, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(context);
        var page = new TiffPage(directory, index, context.IsLenient);
        page.ResolveGeometry(context);
        page.ResolveSamples();
        page.ResolveCompression();
        page.ResolveBlocks(context);
        page.ResolveMetadata(context);
        return page;
    }

    /// <summary>Gets the number of bytes of one full row of the decoded page, padded to a whole number of bytes.</summary>
    public long RowLength => (((long)Size.Width * BitsPerPixel) + 7) / 8;

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
        var isJpeg = Directory.TryGetUnsigned(TiffFormat.CompressionTag, out var compression) && compression == TiffFormat.CompressionJpeg;
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

        if (photometric is not (TiffFormat.PhotometricWhiteIsZero or TiffFormat.PhotometricBlackIsZero or TiffFormat.PhotometricRgb or TiffFormat.PhotometricPalette or TiffFormat.PhotometricCmyk or TiffFormat.PhotometricYCbCr))
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses the {TiffFormat.GetPhotometricName((long)photometric)} photometric interpretation; WhiteIsZero, BlackIsZero, RGB, palette color, CMYK and YCbCr are supported."), string.Create(CultureInfo.InvariantCulture, $"PhotometricInterpretation: {photometric}"));

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
            case TiffFormat.PhotometricRgb or TiffFormat.PhotometricCmyk when BitsPerSample < 8:
                throw UnsupportedBitsPerSample(bits[0], string.Create(CultureInfo.InvariantCulture, $"{TiffFormat.GetPhotometricName(Photometric)} data is supported with 8 and 16 bits"));

            case TiffFormat.PhotometricPalette when BitsPerSample > 8:
                throw UnsupportedBitsPerSample(bits[0], "palette color data is supported with 1, 2, 4 and 8 bits");

            case TiffFormat.PhotometricYCbCr when BitsPerSample != 8 && !(isJpeg && BitsPerSample > 8):
                throw UnsupportedBitsPerSample(bits[0], "YCbCr data is supported with 8 bits, and with 9 to 16 bits with JPEG compression");
        }

        var colorSamples = Photometric switch
        {
            TiffFormat.PhotometricRgb or TiffFormat.PhotometricYCbCr => 3,
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
                throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} stores an alpha channel with {BitsPerSample}-bit {TiffFormat.GetPhotometricName(Photometric)} data; alpha is supported with 8- and 16-bit grayscale, RGB and CMYK data."), "ExtraSamples: alpha with this sample layout");
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
        }
    }

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
            or TiffFormat.CompressionJpeg or TiffFormat.CompressionAdobeDeflate or TiffFormat.CompressionPackBits or TiffFormat.CompressionDeflate or TiffFormat.CompressionJpeg2000))
        {
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses {TiffFormat.GetCompressionName((long)compression)} compression; uncompressed, Deflate, LZW, PackBits, CCITT (modified Huffman, Group 3 and Group 4), JPEG and JPEG 2000 data are supported."), string.Create(CultureInfo.InvariantCulture, $"Compression: {compression}"));
        }

        Compression = (int)compression;

        // With one sample per pixel there is one plane, however it is called
        var planar = Directory.TryGetUnsigned(TiffFormat.PlanarConfigurationTag, out var planarValue) ? planarValue : TiffFormat.PlanarChunky;
        if (planar != TiffFormat.PlanarChunky && !(planar == TiffFormat.PlanarSeparate && SamplesPerPixel == 1))
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses the planar configuration {planar}; only chunky (1), where the samples of a pixel are contiguous, is supported."), string.Create(CultureInfo.InvariantCulture, $"PlanarConfiguration: {planar}"));

        // TIFF 6.0 only allows the reversed order for bilevel data (uncompressed or CCITT), where it is the order of fax devices
        var fillOrder = Directory.TryGetUnsigned(TiffFormat.FillOrderTag, out var fillOrderValue) ? fillOrderValue : TiffFormat.FillOrderMostSignificantBitFirst;
        if (fillOrder != TiffFormat.FillOrderMostSignificantBitFirst && !(fillOrder == TiffFormat.FillOrderLeastSignificantBitFirst && BitsPerPixel == 1))
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses the fill order {fillOrder}; 1 (most significant bit first) is supported, and 2 for bilevel data."), string.Create(CultureInfo.InvariantCulture, $"FillOrder: {fillOrder}"));

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

        if (IsPalette || IsYCbCr)
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses JPEG 2000 compression with {(IsPalette ? "palette indexes" : "YCbCr data")}; grayscale, RGB and CMYK data are supported."), "Compression: JPEG 2000 with this sample layout");
    }

    private void ResolveJpeg()
    {
        // The datastream of every block must have this precision: 8 or 12 bits, or any of them when it is lossless
        if (BitsPerSample < 8)
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses JPEG compression with {BitsPerSample}-bit samples; samples of 8 to 16 bits are supported."), string.Create(CultureInfo.InvariantCulture, $"Compression: JPEG with {BitsPerSample}-bit samples"));

        if (IsPalette || HasAlpha)
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses JPEG compression with {(IsPalette ? "palette indexes" : "an alpha channel")}; grayscale, RGB, YCbCr and CMYK data without extra sample are supported."), "Compression: JPEG with this sample layout");

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
        ushort offsetsTag, byteCountsTag;
        if (IsTiled)
        {
            var tileWidth = GetRequiredUnsigned(TiffFormat.TileWidthTag, "TileWidth");
            var tileLength = GetRequiredUnsigned(TiffFormat.TileLengthTag, "TileLength");
            if (tileWidth == 0 || tileLength == 0 || tileWidth > int.MaxValue || tileLength > int.MaxValue)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} declares {tileWidth}x{tileLength} tiles; both dimensions must be positive and representable."));

            if (tileWidth % 16 != 0 || tileLength % 16 != 0)
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
            if (!hasStripOffsets)
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

        // A row is held as stored samples, as pixels of the working representation, and for 16-bit inks as five native
        // samples per pixel on the way to their conversion
        var blockRowLength = (((long)BlockWidth * BitsPerPixel) + 7) / 8;
        var expandedRowLength = (long)Size.Width * Math.Max(PixelFormats.GetBytesPerPixel(SourcePixelFormat), IsCmyk ? 5 * sizeof(ushort) : 0);
        if (blockRowLength > CheckedSizes.MaxBufferLength || RowLength > CheckedSizes.MaxBufferLength || expandedRowLength > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(context.Limits);

        BlockRowLength = (int)blockRowLength;
        if (!CheckedSizes.TryMultiply(blockRowLength, BlockHeight, out var blockLength) || blockLength > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(context.Limits);

        var blockCount = (long)BlocksAcross * BlocksDown;
        var offsetsField = Directory.Find(offsetsTag)!.Value;
        BlockOffsets = Directory.ReadUnsignedArray(offsetsField, blockCount);

        // Leniently, the one uncompressed block of a page is as long as its geometry says when the byte counts are missing
        // or, for a strip, too small, as libtiff, Pillow and Apple ImageIO read it: these bytes are in the file, and a block
        // that runs past its end is still an error. Byte counts beyond the number of blocks are ignored, as they all do
        var hasGeometricLength = _isLenient && blockCount == 1 && Compression == TiffFormat.CompressionNone && YCbCr is null;
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

        if (Directory.TryReadBytes(TiffFormat.IccProfileTag, context.Limits.MaxMetadataBytes) is { } icc)
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
