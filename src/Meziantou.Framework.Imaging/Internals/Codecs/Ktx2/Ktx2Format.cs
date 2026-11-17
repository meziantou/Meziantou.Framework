namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The constants of the KTX 2.0 file format (KTX File Format Specification, version 2.0, revision 4) and of the parts of
/// the Khronos Data Format Specification (version 1.4) it uses: the identifier, the fixed layout of the header and of the
/// index, the supercompression schemes, the reserved keys of the key/value data, and the enumerations of the basic data
/// format descriptor block.
/// </summary>
internal static class Ktx2Format
{
    /// <summary>The length of the header and the index before the level index (section 2): the identifier, 9 integers and the index.</summary>
    public const int HeaderLength = 80;

    /// <summary>The length of one entry of the level index (section 3.9.7): three 64-bit integers.</summary>
    public const int LevelIndexEntryLength = 24;

    /// <summary>The length of the BasisLZ global data before the image descriptors (Appendix C).</summary>
    public const int BasisLzGlobalHeaderLength = 20;

    /// <summary>The length of one image descriptor of the BasisLZ global data (Appendix C.7).</summary>
    public const int BasisLzImageDescLength = 20;

    /// <summary>The <c>isPFrame</c> flag of an image descriptor (Appendix C.7.1).</summary>
    public const uint BasisLzPFrameFlag = 0x02;

    /// <summary>The supercompression scheme 0: none (Table 2).</summary>
    public const uint SchemeNone = 0;

    /// <summary>The supercompression scheme 1: BasisLZ (ETC1S slices and global codebooks).</summary>
    public const uint SchemeBasisLz = 1;

    /// <summary>The supercompression scheme 2: Zstandard (RFC 8878).</summary>
    public const uint SchemeZstandard = 2;

    /// <summary>The supercompression scheme 3: ZLIB (RFC 1950).</summary>
    public const uint SchemeZlib = 3;

    /// <summary>The key of the orientation of the texture (section 5.2).</summary>
    public const string OrientationKey = "KTXorientation";

    /// <summary>The key of the component mapping (section 5.4).</summary>
    public const string SwizzleKey = "KTXswizzle";

    /// <summary>The key of the writer of the file (section 5.5).</summary>
    public const string WriterKey = "KTXwriter";

    /// <summary>The key of the options of the writer (section 5.6).</summary>
    public const string WriterScParamsKey = "KTXwriterScParams";

    /// <summary>The key of the animation of an array texture (section 5.8).</summary>
    public const string AnimDataKey = "KTXanimData";

    /// <summary>The key of the faces of an incomplete cube map (section 5.1).</summary>
    public const string CubemapIncompleteKey = "KTXcubemapIncomplete";

    /// <summary>The key of the ASTC decode mode (section 5.7).</summary>
    public const string AstcDecodeModeKey = "KTXastcDecodeMode";

    // Khronos Data Format Specification, section 5.5 and 5.6: the color models
    public const int ModelRgbsda = 1;
    public const int ModelYuvsda = 2;
    public const int ModelBc1A = 128;
    public const int ModelBc2 = 129;
    public const int ModelBc3 = 130;
    public const int ModelBc4 = 131;
    public const int ModelBc5 = 132;
    public const int ModelBc6H = 133;
    public const int ModelBc7 = 134;
    public const int ModelEtc1 = 160;
    public const int ModelEtc2 = 161;
    public const int ModelAstc = 162;
    public const int ModelEtc1S = 163;
    public const int ModelPvrtc = 164;
    public const int ModelPvrtc2 = 165;
    public const int ModelUastc = 166;

    /// <summary>The color model basisu 2 writes for its UASTC HDR blocks, which the KTX 2.0 specification does not define.</summary>
    public const int ModelUastcHdr = 167;

    // Section 5.7: the color primaries
    public const int PrimariesUnspecified = 0;
    public const int PrimariesBt709 = 1;
    public const int PrimariesBt601Ebu = 2;
    public const int PrimariesBt601Smpte = 3;
    public const int PrimariesBt2020 = 4;
    public const int PrimariesCieXyz = 5;
    public const int PrimariesAces = 6;
    public const int PrimariesAcesCc = 7;
    public const int PrimariesNtsc1953 = 8;
    public const int PrimariesPal525 = 9;
    public const int PrimariesDisplayP3 = 10;
    public const int PrimariesAdobeRgb = 11;

    // Section 5.8: the transfer functions
    public const int TransferUnspecified = 0;
    public const int TransferLinear = 1;
    public const int TransferSrgb = 2;
    public const int TransferItu = 3;
    public const int TransferNtsc = 4;
    public const int TransferSlog = 5;
    public const int TransferSlog2 = 6;
    public const int TransferBt1886 = 7;
    public const int TransferHlgOetf = 8;
    public const int TransferHlgEotf = 9;
    public const int TransferPqEotf = 10;
    public const int TransferPqOetf = 11;

    /// <summary>The flag of premultiplied alpha (section 5.9.1).</summary>
    public const int FlagAlphaPremultiplied = 1;

    // Section 5.15: the qualifiers of a sample (the upper four bits of its channel type)
    public const int QualifierLinear = 0x10;
    public const int QualifierExponent = 0x20;
    public const int QualifierSigned = 0x40;
    public const int QualifierFloat = 0x80;

    // The channel ids of the RGBSDA model
    public const int ChannelRed = 0;
    public const int ChannelGreen = 1;
    public const int ChannelBlue = 2;
    public const int ChannelAlpha = 15;

    // The channel ids of the ETC1S model (KTX section 3.10.3)
    public const int Etc1SChannelRgb = 0;
    public const int Etc1SChannelRrr = 3;
    public const int Etc1SChannelGgg = 4;
    public const int Etc1SChannelAaa = 15;

    // The channel ids of the UASTC model (KTX section 3.10.3)
    public const int UastcChannelRgb = 0;
    public const int UastcChannelRgba = 3;
    public const int UastcChannelRrr = 4;
    public const int UastcChannelRrrg = 5;
    public const int UastcChannelRg = 6;

    /// <summary>Gets the 12 bytes of the file identifier (section 3.1): «KTX 20», CR, LF, Ctrl-Z, LF.</summary>
    public static ReadOnlySpan<byte> Identifier => [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Determines whether a prefix starts with the identifier of a KTX 2.0 file.</summary>
    public static bool MatchesSignature(ReadOnlySpan<byte> prefix) => prefix.StartsWith(Identifier);

    /// <summary>Multiplies two sizes of the texture, which the limits keep far from overflowing.</summary>
    public static long Multiply(long left, long right) => CheckedSizes.TryMultiply(left, right, out var result) ? result : throw Invalid("a size of the texture overflows.");

    /// <summary>Gets a buffer length, or throws when it is longer than a buffer can be.</summary>
    public static int ToBufferLength(long length, ImageResourceLimits limits) => length <= CheckedSizes.MaxBufferLength ? (int)length : throw CheckedSizes.CreateOverflowException(limits);

    public static InvalidImageContentException Invalid(string message) => new("The KTX 2.0 file is invalid: " + message, ImageFormat.Ktx2);

    public static UnsupportedImageFeatureException Unsupported(string message, string feature) => new(message, ImageFormat.Ktx2, feature);

    /// <summary>Gets the name of a supercompression scheme, for messages.</summary>
    public static string GetSchemeName(uint scheme) => scheme switch
    {
        SchemeNone => "none",
        SchemeBasisLz => "BasisLZ",
        SchemeZstandard => "Zstandard",
        SchemeZlib => "ZLIB",
        _ => "0x" + scheme.ToString("X", CultureInfo.InvariantCulture),
    };
}
