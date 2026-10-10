namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The constants of the Truevision TGA formats (TGA File Format Specification 2.0) shared by the decoder and the encoder.
/// </summary>
/// <remarks>
/// TGA has no signature at the start of the file: it is recognized by the strict plausibility of its 18-byte header
/// (<see cref="TgaHeader.IsPlausible"/>), which is why the TGA codec is the last one consulted by the registry.
/// </remarks>
internal static class TgaFormat
{
    /// <summary>The length of the fixed header.</summary>
    public const int HeaderLength = 18;

    /// <summary>The length of the TGA 2.0 footer: the extension and developer offsets, the signature, a dot and a NUL byte.</summary>
    public const int FooterLength = 26;

    /// <summary>The length of the TGA 2.0 extension area.</summary>
    public const int ExtensionAreaLength = 495;

    /// <summary>The offset of the attributes type inside the extension area.</summary>
    public const int ExtensionAttributesTypeOffset = 494;

    /// <summary>The attributes type of an alpha channel whose color samples are premultiplied by it.</summary>
    public const byte AttributesTypePremultipliedAlpha = 4;

    /// <summary>The largest number of pixels one run-length or raw packet encodes.</summary>
    public const int MaxPacketLength = 128;

    /// <summary>The bit of a packet header that marks a run-length packet.</summary>
    public const byte RunLengthPacketFlag = 0x80;

    /// <summary>The mask of the alpha-channel bit count of the image descriptor.</summary>
    public const byte DescriptorAlphaBitsMask = 0x0F;

    /// <summary>The image-descriptor bit set when the columns are stored right to left.</summary>
    public const byte DescriptorRightToLeft = 0x10;

    /// <summary>The image-descriptor bit set when the rows are stored top to bottom.</summary>
    public const byte DescriptorTopToBottom = 0x20;

    /// <summary>The image-descriptor bits reserved by the specification (they must be zero).</summary>
    public const byte DescriptorReservedMask = 0xC0;

    /// <summary>The 18-byte signature of the TGA 2.0 footer (<c>TRUEVISION-XFILE.</c> and a NUL byte).</summary>
    public static ReadOnlySpan<byte> FooterSignature => "TRUEVISION-XFILE.\0"u8;

    /// <summary>Creates the exception reported for malformed TGA data.</summary>
    public static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.Tga);

    /// <summary>Creates the exception reported for a recognized but unsupported TGA variant.</summary>
    public static UnsupportedImageFeatureException Unsupported(string message, string feature) => new(message, ImageFormat.Tga, feature);
}
