namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The constants of the TIFF 6.0 and BigTIFF container formats shared by the structure reader, the page decoder
/// and the encoder: the header layout, the tags and field types read, and the values of the tags whose support is
/// explicitly scoped.
/// </summary>
internal static class TiffFormat
{
    /// <summary>The length of a classic TIFF header: byte order, magic 42 and the 32-bit offset of the first IFD.</summary>
    public const int ClassicHeaderLength = 8;

    /// <summary>The length of a BigTIFF header: byte order, magic 43, offset size, reserved and the 64-bit offset of the first IFD.</summary>
    public const int BigTiffHeaderLength = 16;

    /// <summary>The magic number of a classic TIFF (42).</summary>
    public const ushort ClassicMagic = 42;

    /// <summary>The magic number of a BigTIFF (43).</summary>
    public const ushort BigTiffMagic = 43;

    /// <summary>The length of one classic IFD entry: tag, type, 32-bit count and a 4-byte inline value or offset.</summary>
    public const int ClassicEntryLength = 12;

    /// <summary>The length of one BigTIFF IFD entry: tag, type, 64-bit count and an 8-byte inline value or offset.</summary>
    public const int BigTiffEntryLength = 20;

    /// <summary>The <c>II</c> byte-order mark of a little-endian TIFF.</summary>
    public static ReadOnlySpan<byte> LittleEndianMark => "II"u8;

    /// <summary>The <c>MM</c> byte-order mark of a big-endian TIFF.</summary>
    public static ReadOnlySpan<byte> BigEndianMark => "MM"u8;

    // Baseline tags read by the decoder; every other tag is skipped without being interpreted
    public const ushort NewSubfileTypeTag = 254;
    public const ushort SubfileTypeTag = 255;
    public const ushort ImageWidthTag = 256;
    public const ushort ImageLengthTag = 257;
    public const ushort BitsPerSampleTag = 258;
    public const ushort CompressionTag = 259;
    public const ushort PhotometricInterpretationTag = 262;
    public const ushort FillOrderTag = 266;
    public const ushort DocumentNameTag = 269;
    public const ushort ImageDescriptionTag = 270;
    public const ushort StripOffsetsTag = 273;
    public const ushort OrientationTag = 274;
    public const ushort SamplesPerPixelTag = 277;
    public const ushort RowsPerStripTag = 278;
    public const ushort StripByteCountsTag = 279;
    public const ushort XResolutionTag = 282;
    public const ushort YResolutionTag = 283;
    public const ushort PlanarConfigurationTag = 284;
    public const ushort PageNameTag = 285;
    public const ushort ResolutionUnitTag = 296;
    public const ushort PageNumberTag = 297;
    public const ushort SoftwareTag = 305;
    public const ushort PredictorTag = 317;
    public const ushort ColorMapTag = 320;
    public const ushort TileWidthTag = 322;
    public const ushort TileLengthTag = 323;
    public const ushort TileOffsetsTag = 324;
    public const ushort TileByteCountsTag = 325;
    public const ushort ExtraSamplesTag = 338;
    public const ushort SampleFormatTag = 339;
    public const ushort JpegTablesTag = 347;
    public const ushort XmpTag = 700;
    public const ushort ExifIfdPointerTag = 34665;
    public const ushort IccProfileTag = 34675;

    // PhotometricInterpretation values
    public const int PhotometricWhiteIsZero = 0;
    public const int PhotometricBlackIsZero = 1;
    public const int PhotometricRgb = 2;
    public const int PhotometricPalette = 3;
    public const int PhotometricTransparencyMask = 4;
    public const int PhotometricCmyk = 5;
    public const int PhotometricYCbCr = 6;
    public const int PhotometricCieLab = 8;

    // Compression values
    public const int CompressionNone = 1;
    public const int CompressionCcittRle = 2;
    public const int CompressionCcittFax3 = 3;
    public const int CompressionCcittFax4 = 4;
    public const int CompressionLzw = 5;
    public const int CompressionOldJpeg = 6;
    public const int CompressionJpeg = 7;
    public const int CompressionAdobeDeflate = 8;
    public const int CompressionPackBits = 32773;
    public const int CompressionDeflate = 32946;

    // PlanarConfiguration values
    public const int PlanarChunky = 1;
    public const int PlanarSeparate = 2;

    // Predictor values
    public const int PredictorNone = 1;
    public const int PredictorHorizontalDifferencing = 2;
    public const int PredictorFloatingPoint = 3;

    // SampleFormat values
    public const int SampleFormatUnsignedInteger = 1;
    public const int SampleFormatSignedInteger = 2;
    public const int SampleFormatFloat = 3;
    public const int SampleFormatUndefined = 4;

    // ExtraSamples values
    public const int ExtraSampleUnspecified = 0;
    public const int ExtraSampleAssociatedAlpha = 1;
    public const int ExtraSampleUnassociatedAlpha = 2;

    // ResolutionUnit values
    public const int ResolutionUnitNone = 1;
    public const int ResolutionUnitInch = 2;
    public const int ResolutionUnitCentimeter = 3;

    /// <summary>Creates the exception reported for malformed TIFF data.</summary>
    public static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.Tiff);

    /// <summary>Creates the exception reported for a recognized but unsupported TIFF variant.</summary>
    public static UnsupportedImageFeatureException Unsupported(string message, string feature) => new(message, ImageFormat.Tiff, feature);

    /// <summary>Gets the name of a compression value, for error messages.</summary>
    public static string GetCompressionName(long compression) => compression switch
    {
        CompressionNone => "uncompressed",
        CompressionCcittRle => "CCITT modified Huffman RLE",
        CompressionCcittFax3 => "CCITT Group 3 fax",
        CompressionCcittFax4 => "CCITT Group 4 fax",
        CompressionLzw => "LZW",
        CompressionOldJpeg => "old-style JPEG",
        CompressionJpeg => "JPEG",
        CompressionAdobeDeflate => "Adobe Deflate",
        CompressionPackBits => "PackBits",
        CompressionDeflate => "Deflate",
        _ => string.Create(CultureInfo.InvariantCulture, $"compression {compression}"),
    };

    /// <summary>Gets the name of a photometric interpretation, for error messages.</summary>
    public static string GetPhotometricName(long photometric) => photometric switch
    {
        PhotometricWhiteIsZero => "WhiteIsZero grayscale",
        PhotometricBlackIsZero => "BlackIsZero grayscale",
        PhotometricRgb => "RGB",
        PhotometricPalette => "palette color",
        PhotometricTransparencyMask => "transparency mask",
        PhotometricCmyk => "CMYK (separated)",
        PhotometricYCbCr => "YCbCr",
        PhotometricCieLab => "CIE L*a*b*",
        _ => string.Create(CultureInfo.InvariantCulture, $"photometric interpretation {photometric}"),
    };
}
