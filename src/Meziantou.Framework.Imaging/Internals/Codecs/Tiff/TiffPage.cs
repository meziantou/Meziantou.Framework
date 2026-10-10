using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// One resolved and validated TIFF page: the geometry, the sample layout, the compression, the strip or tile table and the
/// metadata of a single image file directory. A page is resolved without reading a single
/// pixel byte, so a collection can describe every page of a document without decoding any of them.
/// </summary>
/// <remarks>
/// <para>
/// The supported subset is deliberately narrow and is checked here, once: unsigned integer samples of 8 or 16 bits,
/// 1 (grayscale), 2 (grayscale and alpha), 3 (RGB) or 4 (RGB and alpha) samples per pixel, the
/// <c>WhiteIsZero</c>, <c>BlackIsZero</c> and <c>RGB</c> photometric interpretations, chunky planar configuration, strips
/// or tiles, no compression or Deflate, and the horizontal differencing predictor. Everything else is recognized and
/// rejected with <see cref="UnsupportedImageFeatureException"/>, never guessed.
/// </para>
/// <para>
/// Alpha is only an alpha channel when <c>ExtraSamples</c> says so and says it is unassociated (straight): the working
/// pixel formats store straight alpha, so associated (premultiplied) alpha is rejected instead of being silently divided
/// out, and an unspecified extra sample is not transparency.
/// </para>
/// </remarks>
internal sealed class TiffPage
{
    private TiffPage(TiffDirectory directory, int index)
    {
        Directory = directory;
        Index = index;
    }

    /// <summary>Gets the directory this page was resolved from.</summary>
    public TiffDirectory Directory { get; }

    /// <summary>Gets the zero-based index of the page in the document.</summary>
    public int Index { get; }

    /// <summary>Gets a value indicating whether the samples are stored in big-endian byte order.</summary>
    public bool IsBigEndian => Directory.Header.IsBigEndian;

    /// <summary>Gets the page size, in pixels.</summary>
    public Size Size { get; private set; }

    /// <summary>Gets the number of bits of one sample: 8 or 16.</summary>
    public int BitsPerSample { get; private set; }

    /// <summary>Gets the number of samples of one pixel: 1, 2, 3 or 4.</summary>
    public int SamplesPerPixel { get; private set; }

    /// <summary>Gets the photometric interpretation (0, 1 or 2).</summary>
    public int Photometric { get; private set; }

    /// <summary>Gets a value indicating whether 0 is white (<c>WhiteIsZero</c>): the decoder inverts the samples.</summary>
    public bool IsWhiteIsZero => Photometric == TiffFormat.PhotometricWhiteIsZero;

    /// <summary>Gets a value indicating whether the samples are grayscale.</summary>
    public bool IsGrayscale => Photometric is TiffFormat.PhotometricWhiteIsZero or TiffFormat.PhotometricBlackIsZero;

    /// <summary>Gets a value indicating whether the last sample is a straight alpha channel.</summary>
    public bool HasAlpha { get; private set; }

    /// <summary>Gets the compression (1, 8 or 32946).</summary>
    public int Compression { get; private set; }

    /// <summary>Gets a value indicating whether the strips or tiles are Deflate compressed.</summary>
    public bool IsDeflate => Compression is TiffFormat.CompressionAdobeDeflate or TiffFormat.CompressionDeflate;

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

    /// <summary>Gets the number of bytes of one full row of a block, padded to a whole number of bytes.</summary>
    public int BlockRowLength { get; private set; }

    /// <summary>Gets the page metadata: resolution, orientation, ICC profile and XMP packet.</summary>
    public ImageMetadata Metadata { get; private set; } = new();

    /// <summary>Gets the lossless working representation of the samples.</summary>
    public PixelFormat SourcePixelFormat => DefaultPixelFormats.ForTiff(SamplesPerPixel, BitsPerSample);

    /// <summary>Gets the color model reported by identification.</summary>
    public ImageColorModel ColorModel => IsGrayscale ? (HasAlpha ? ImageColorModel.GrayscaleAlpha : ImageColorModel.Grayscale) : (HasAlpha ? ImageColorModel.Rgba : ImageColorModel.Rgb);

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
        var page = new TiffPage(directory, index);
        page.ResolveGeometry(context);
        page.ResolveSamples();
        page.ResolveCompression();
        page.ResolveBlocks(context);
        page.ResolveMetadata(context);
        return page;
    }

    /// <summary>Gets the number of bytes of one full row of the page.</summary>
    public long RowLength => ((long)Size.Width * SamplesPerPixel * BitsPerSample) / 8;

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
        if (samplesPerPixel is 0 or > 4)
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} stores {samplesPerPixel} samples per pixel; 1 (grayscale), 2 (grayscale and alpha), 3 (RGB) and 4 (RGB and alpha) are supported."), "SamplesPerPixel");

        SamplesPerPixel = (int)samplesPerPixel;

        var bits = Directory.TryReadUnsignedArray(TiffFormat.BitsPerSampleTag, SamplesPerPixel) ?? [8];
        if (bits.Length != SamplesPerPixel)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} declares {bits.Length} BitsPerSample values for {SamplesPerPixel} samples per pixel."));

        foreach (var value in bits)
        {
            if (value != bits[0])
                throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} mixes sample widths ({string.Join(", ", bits)} bits); every sample must have the same width."), "BitsPerSample: mixed widths");
        }

        if (bits[0] is not (8 or 16))
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} stores {bits[0]}-bit samples; 8 and 16 bits are supported."), string.Create(CultureInfo.InvariantCulture, $"BitsPerSample: {bits[0]}"));

        BitsPerSample = (int)bits[0];

        var sampleFormats = Directory.TryReadUnsignedArray(TiffFormat.SampleFormatTag, SamplesPerPixel);
        if (sampleFormats is not null)
        {
            foreach (var format in sampleFormats)
            {
                if (format is not (TiffFormat.SampleFormatUnsignedInteger or TiffFormat.SampleFormatUndefined))
                {
                    var name = format == TiffFormat.SampleFormatSignedInteger ? "signed integer" : format == TiffFormat.SampleFormatFloat ? "floating-point" : string.Create(CultureInfo.InvariantCulture, $"SampleFormat {format}");
                    throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} stores {name} samples; only unsigned integer samples are supported."), string.Create(CultureInfo.InvariantCulture, $"SampleFormat: {format}"));
                }
            }
        }

        if (!Directory.TryGetUnsigned(TiffFormat.PhotometricInterpretationTag, out var photometric))
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} has no PhotometricInterpretation tag, which is required."));

        if (photometric is not (TiffFormat.PhotometricWhiteIsZero or TiffFormat.PhotometricBlackIsZero or TiffFormat.PhotometricRgb))
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses the {TiffFormat.GetPhotometricName((long)photometric)} photometric interpretation; WhiteIsZero, BlackIsZero and RGB are supported."), string.Create(CultureInfo.InvariantCulture, $"PhotometricInterpretation: {photometric}"));

        Photometric = (int)photometric;

        var colorSamples = Photometric == TiffFormat.PhotometricRgb ? 3 : 1;
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
        }
    }

    private void ResolveCompression()
    {
        var compression = Directory.TryGetUnsigned(TiffFormat.CompressionTag, out var value) ? value : TiffFormat.CompressionNone;
        if (compression is not (TiffFormat.CompressionNone or TiffFormat.CompressionAdobeDeflate or TiffFormat.CompressionDeflate))
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses {TiffFormat.GetCompressionName((long)compression)} compression; uncompressed and Deflate data are supported."), string.Create(CultureInfo.InvariantCulture, $"Compression: {compression}"));

        Compression = (int)compression;

        var planar = Directory.TryGetUnsigned(TiffFormat.PlanarConfigurationTag, out var planarValue) ? planarValue : TiffFormat.PlanarChunky;
        if (planar != TiffFormat.PlanarChunky)
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses the planar configuration {planar}; only chunky (1), where the samples of a pixel are contiguous, is supported."), string.Create(CultureInfo.InvariantCulture, $"PlanarConfiguration: {planar}"));

        var fillOrder = Directory.TryGetUnsigned(TiffFormat.FillOrderTag, out var fillOrderValue) ? fillOrderValue : 1;
        if (fillOrder != 1)
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses the reversed fill order {fillOrder}; only 1 (most significant bit first) is supported."), string.Create(CultureInfo.InvariantCulture, $"FillOrder: {fillOrder}"));

        var predictor = Directory.TryGetUnsigned(TiffFormat.PredictorTag, out var predictorValue) ? predictorValue : TiffFormat.PredictorNone;
        if (predictor is not (TiffFormat.PredictorNone or TiffFormat.PredictorHorizontalDifferencing))
        {
            var name = predictor == TiffFormat.PredictorFloatingPoint ? "the floating-point predictor" : string.Create(CultureInfo.InvariantCulture, $"predictor {predictor}");
            throw TiffFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} uses {name}; no predictor (1) and horizontal differencing (2) are supported."), string.Create(CultureInfo.InvariantCulture, $"Predictor: {predictor}"));
        }

        Predictor = (int)predictor;
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
        }

        var blockRowLength = ((long)BlockWidth * SamplesPerPixel * BitsPerSample) / 8;
        if (blockRowLength > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(context.Limits);

        BlockRowLength = (int)blockRowLength;
        if (!CheckedSizes.TryMultiply(blockRowLength, BlockHeight, out var blockLength) || blockLength > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(context.Limits);

        var blockCount = (long)BlocksAcross * BlocksDown;
        var offsetsField = Directory.Find(offsetsTag)!.Value;
        BlockOffsets = Directory.ReadUnsignedArray(offsetsField, blockCount);
        if (Directory.Find(byteCountsTag) is not { } byteCountsField)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {Index} declares {(IsTiled ? "TileOffsets" : "StripOffsets")} without the matching byte counts."));

        BlockByteCounts = Directory.ReadUnsignedArray(byteCountsField, blockCount);
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
            builder.Charge(icc.Length);
            builder.TryAdoptIccProfile(icc, IsGrayscale);
        }

        if (Directory.TryReadBytes(TiffFormat.XmpTag, context.Limits.MaxMetadataBytes) is { } xmp)
        {
            builder.TryAdoptXmp(xmp);
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
