namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The constants of the OpenEXR file layout (OpenEXR File Layout, OpenEXR 2.0 and later) shared by the decoder and the
/// encoder, and the errors of the format.
/// </summary>
/// <remarks>
/// The compression methods follow the description of their data formats written for this library
/// (<c>ExrCompression.md</c> next to this file), which cites its sections by number.
/// </remarks>
internal static class ExrFormat
{
    /// <summary>The magic number 20000630, stored as a little-endian <c>int</c>.</summary>
    public const int MagicNumber = 20000630;

    /// <summary>The file format version of the low byte of the version field.</summary>
    public const int Version = 2;

    /// <summary>Bit 9 of the version field: a single-part tiled image.</summary>
    public const int SingleTileFlag = 0x200;

    /// <summary>Bit 10 of the version field: attribute and channel names may have up to 255 bytes instead of 31.</summary>
    public const int LongNamesFlag = 0x400;

    /// <summary>Bit 11 of the version field: the file has deep data (a part that is not a regular image).</summary>
    public const int NonImageFlag = 0x800;

    /// <summary>Bit 12 of the version field: the file has several parts.</summary>
    public const int MultipartFlag = 0x1000;

    /// <summary>The flags of the version field that are defined.</summary>
    public const int KnownFlags = SingleTileFlag | LongNamesFlag | NonImageFlag | MultipartFlag;

    /// <summary>The longest attribute, attribute type or channel name without the long name flag.</summary>
    public const int ShortNameMaxLength = 31;

    /// <summary>The longest attribute, attribute type or channel name with the long name flag.</summary>
    public const int LongNameMaxLength = 255;

    /// <summary>The length of the magic number and the version field.</summary>
    public const int PreambleLength = 8;

    /// <summary>The length of the fields before the data of a scan-line chunk: its y coordinate and the size of its data.</summary>
    public const int ScanLineChunkHeaderLength = 8;

    /// <summary>The length of the fields before the data of a tile: four tile coordinates and the size of its data.</summary>
    public const int TileChunkHeaderLength = 20;

    /// <summary>The first four bytes of every OpenEXR file: the magic number 20000630 in little-endian order.</summary>
    public static ReadOnlySpan<byte> Magic => [0x76, 0x2F, 0x31, 0x01];

    /// <summary>Gets the number of scan lines of a chunk of a scan-line image (section 0 of the compression notes).</summary>
    public static int GetLinesPerChunk(ExrCompressionMethod compression) => compression switch
    {
        ExrCompressionMethod.None or ExrCompressionMethod.Rle or ExrCompressionMethod.Zips or ExrCompressionMethod.Zstd => 1,
        ExrCompressionMethod.Zip or ExrCompressionMethod.Pxr24 => 16,
        ExrCompressionMethod.Piz or ExrCompressionMethod.B44 or ExrCompressionMethod.B44A or ExrCompressionMethod.Dwaa or ExrCompressionMethod.Htj2k32 => 32,
        ExrCompressionMethod.Dwab or ExrCompressionMethod.Htj2k256 or ExrCompressionMethod.Lj2k => 256,
        _ => throw new ArgumentOutOfRangeException(nameof(compression), compression, "The compression is not valid."),
    };

    /// <summary>Gets the number of bytes of one sample of a pixel type.</summary>
    public static int GetBytesPerSample(ExrPixelType type) => type == ExrPixelType.Half ? 2 : 4;

    /// <summary>Gets the name of a compression method, as the OpenEXR documentation writes it.</summary>
    public static string GetName(ExrCompressionMethod compression) => compression switch
    {
        ExrCompressionMethod.None => "NONE",
        ExrCompressionMethod.Rle => "RLE",
        ExrCompressionMethod.Zips => "ZIPS",
        ExrCompressionMethod.Zip => "ZIP",
        ExrCompressionMethod.Piz => "PIZ",
        ExrCompressionMethod.Pxr24 => "PXR24",
        ExrCompressionMethod.B44 => "B44",
        ExrCompressionMethod.B44A => "B44A",
        ExrCompressionMethod.Dwaa => "DWAA",
        ExrCompressionMethod.Dwab => "DWAB",
        ExrCompressionMethod.Htj2k256 => "HTJ2K256",
        ExrCompressionMethod.Htj2k32 => "HTJ2K32",
        ExrCompressionMethod.Lj2k => "LJ2K",
        ExrCompressionMethod.Zstd => "ZSTD",
        _ => compression.ToString(),
    };

    /// <summary>Determines whether the library decodes the chunks of a compression method.</summary>
    public static bool IsDecodable(ExrCompressionMethod compression) => compression <= ExrCompressionMethod.B44A;

    public static InvalidImageContentException Invalid(string message) => new("The OpenEXR file is invalid: " + message, ImageFormat.OpenExr);

    public static UnsupportedImageFeatureException Unsupported(string message, string feature) => new(message, ImageFormat.OpenExr, feature);
}
