namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Constants and error factories of the JPEG XL codec (ISO/IEC 18181-1 for the codestream, ISO/IEC 18181-2 for the container).</summary>
internal static class JpegXlFormat
{
    /// <summary>The length of a box header: the 32-bit size and the type.</summary>
    public const int BoxHeaderLength = 8;

    /// <summary>The length of a box header whose size is stored in the 64-bit field.</summary>
    public const int LargeBoxHeaderLength = 16;

    /// <summary>The <c>JXL </c> code: the signature box.</summary>
    public const uint SignatureBox = 0x4A584C20;

    /// <summary>The <c>ftyp</c> code.</summary>
    public const uint FileTypeBox = 0x66747970;

    /// <summary>The <c>jxl </c> code: the brand of the file type box.</summary>
    public const uint Brand = 0x6A786C20;

    /// <summary>The <c>jxll</c> code: the level of the codestream.</summary>
    public const uint LevelBox = 0x6A786C6C;

    /// <summary>The <c>jxlc</c> code: the whole codestream.</summary>
    public const uint CodestreamBox = 0x6A786C63;

    /// <summary>The <c>jxlp</c> code: a part of the codestream.</summary>
    public const uint PartialCodestreamBox = 0x6A786C70;

    /// <summary>The <c>jxli</c> code: the frame index.</summary>
    public const uint FrameIndexBox = 0x6A786C69;

    /// <summary>The <c>Exif</c> code.</summary>
    public const uint ExifBox = 0x45786966;

    /// <summary>The <c>xml </c> code: an XMP packet.</summary>
    public const uint XmlBox = 0x786D6C20;

    /// <summary>The <c>jumb</c> code: a JUMBF superbox.</summary>
    public const uint JumbfBox = 0x6A756D62;

    /// <summary>The <c>brob</c> code: a box compressed with Brotli.</summary>
    public const uint BrotliBox = 0x62726F62;

    /// <summary>The <c>jbrd</c> code: JPEG bitstream reconstruction data.</summary>
    public const uint JpegReconstructionBox = 0x6A627264;

    /// <summary>The bit of a <c>jxlp</c> index that marks the last part of the codestream.</summary>
    public const uint LastPartFlag = 0x80000000;

    /// <summary>The largest number of extra channels a codestream can declare (level 10).</summary>
    public const int MaxExtraChannels = 256;

    /// <summary>The first two bytes of a codestream.</summary>
    public static ReadOnlySpan<byte> CodestreamSignature => [0xFF, 0x0A];

    /// <summary>The first twelve bytes of a container: the signature box.</summary>
    public static ReadOnlySpan<byte> ContainerSignature => [0x00, 0x00, 0x00, 0x0C, 0x4A, 0x58, 0x4C, 0x20, 0x0D, 0x0A, 0x87, 0x0A];

    /// <summary>The file violates the specification.</summary>
    public static InvalidImageContentException Invalid(string message) => new("The JPEG XL file is invalid: " + message, ImageFormat.JpegXl);

    /// <summary>The file is valid but uses a feature that the decoder does not implement or that an image cannot hold.</summary>
    public static UnsupportedImageFeatureException Unsupported(string message, string feature) => new(message, ImageFormat.JpegXl, feature);

    /// <summary>Returns the text of a four-character code, for messages.</summary>
    public static string ToText(uint code)
    {
        Span<char> text = stackalloc char[4];
        for (var i = 0; i < 4; i++)
        {
            var value = (char)((code >> (24 - (i * 8))) & 0xFF);
            text[i] = value is >= ' ' and <= '~' ? value : '?';
        }

        return new string(text);
    }
}
