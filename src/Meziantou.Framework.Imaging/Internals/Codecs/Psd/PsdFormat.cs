namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The constants of the Adobe Photoshop file format (Adobe Photoshop File Formats Specification, "Photoshop File
/// Format"): the signature, the color modes, the compressions of the image data, and the image resources the decoder
/// reads.
/// </summary>
internal static class PsdFormat
{
    /// <summary>The signature of the file header.</summary>
    public static ReadOnlySpan<byte> Signature => "8BPS"u8;

    /// <summary>The length of the file header.</summary>
    public const int HeaderLength = 26;

    /// <summary>The version of a PSD file.</summary>
    public const int PsdVersion = 1;

    /// <summary>The version of a PSB (large document format) file.</summary>
    public const int PsbVersion = 2;

    /// <summary>The largest width or height of a PSD file.</summary>
    public const int MaxPsdDimension = 30_000;

    /// <summary>The largest width or height of a PSB file.</summary>
    public const int MaxPsbDimension = 300_000;

    /// <summary>The largest number of channels of the image data.</summary>
    public const int MaxChannels = 56;

    public const int ModeBitmap = 0;
    public const int ModeGrayscale = 1;
    public const int ModeIndexed = 2;
    public const int ModeRgb = 3;
    public const int ModeCmyk = 4;
    public const int ModeMultichannel = 7;
    public const int ModeDuotone = 8;
    public const int ModeLab = 9;

    public const int CompressionRaw = 0;
    public const int CompressionRle = 1;
    public const int CompressionZip = 2;
    public const int CompressionZipWithPrediction = 3;

    /// <summary>The length of the color table of an indexed image: 256 red, then 256 green, then 256 blue values.</summary>
    public const int ColorTableLength = 768;

    /// <summary>The signature of an image resource block and of most tagged blocks.</summary>
    public static ReadOnlySpan<byte> ResourceSignature => "8BIM"u8;

    /// <summary>The signature of the tagged blocks of a PSB file whose length has 8 bytes.</summary>
    public static ReadOnlySpan<byte> LargeBlockSignature => "8B64"u8;

    public const int ResourceResolutionInfo = 1005;
    public const int ResourceIptc = 1028;
    public const int ResourceIccProfile = 1039;
    public const int ResourceTransparencyIndex = 1047;
    public const int ResourceVersionInfo = 1057;
    public const int ResourceExif = 1058;
    public const int ResourceXmp = 1060;

    /// <summary>Determines whether the data starts with the signature of the file header.</summary>
    public static bool MatchesSignature(ReadOnlySpan<byte> prefix) => prefix.StartsWith(Signature);

    /// <summary>Gets the number of color channels of a color mode.</summary>
    public static int GetColorChannelCount(int mode) => mode switch
    {
        ModeRgb or ModeLab => 3,
        ModeCmyk => 4,
        _ => 1,
    };

    /// <summary>Gets the name of a color mode, for messages.</summary>
    public static string GetModeName(int mode) => mode switch
    {
        ModeBitmap => "bitmap",
        ModeGrayscale => "grayscale",
        ModeIndexed => "indexed",
        ModeRgb => "RGB",
        ModeCmyk => "CMYK",
        ModeMultichannel => "multichannel",
        ModeDuotone => "duotone",
        ModeLab => "Lab",
        _ => "unknown",
    };

    /// <summary>
    /// Determines whether the length field of a tagged block of a PSB file has 8 bytes (the keys the specification lists
    /// under "Additional Layer Information").
    /// </summary>
    public static bool HasLargeLength(ReadOnlySpan<byte> key)
        => key.SequenceEqual("LMsk"u8) || key.SequenceEqual("Lr16"u8) || key.SequenceEqual("Lr32"u8) || key.SequenceEqual("Layr"u8)
        || key.SequenceEqual("Mt16"u8) || key.SequenceEqual("Mt32"u8) || key.SequenceEqual("Mtrn"u8) || key.SequenceEqual("Alph"u8)
        || key.SequenceEqual("FMsk"u8) || key.SequenceEqual("lnk2"u8) || key.SequenceEqual("FEid"u8) || key.SequenceEqual("FXid"u8)
        || key.SequenceEqual("PxSD"u8);

    /// <summary>Determines whether a tagged block holds a layer info structure (layers of 8, 16 or 32-bit samples).</summary>
    public static bool IsLayerInfoBlock(ReadOnlySpan<byte> key)
        => key.SequenceEqual("Layr"u8) || key.SequenceEqual("Lr16"u8) || key.SequenceEqual("Lr32"u8);

    public static InvalidImageContentException Invalid(string message) => new("The PSD file is invalid: " + message, ImageFormat.Psd);

    public static UnsupportedImageFeatureException Unsupported(string message, string feature) => new(message, ImageFormat.Psd, feature);
}
