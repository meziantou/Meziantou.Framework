namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The constants of the DirectDraw Surface format, from the DDS reference of Microsoft Learn ("Programming Guide for DDS",
/// <c>DDS_HEADER</c>, <c>DDS_PIXELFORMAT</c>, <c>DDS_HEADER_DXT10</c>) and the values of <c>DXGI_FORMAT</c>.
/// </summary>
internal static class DdsFormat
{
    /// <summary>The magic number: the four characters <c>DDS </c>.</summary>
    public static ReadOnlySpan<byte> Magic => "DDS "u8;

    /// <summary>The length of the magic number and of <c>DDS_HEADER</c>.</summary>
    public const int HeaderLength = 128;

    /// <summary>The length of <c>DDS_HEADER_DXT10</c>, which follows the header when the FourCC is <c>DX10</c>.</summary>
    public const int Dx10HeaderLength = 20;

    /// <summary>The value of <c>dwSize</c> of <c>DDS_HEADER</c>.</summary>
    public const uint HeaderSize = 124;

    /// <summary>The value of <c>dwSize</c> of <c>DDS_PIXELFORMAT</c>.</summary>
    public const uint PixelFormatSize = 32;

    // DDS_HEADER.dwFlags
    public const uint FlagPitch = 0x8;
    public const uint FlagLinearSize = 0x80000;

    // DDS_PIXELFORMAT.dwFlags
    public const uint PixelAlphaPixels = 0x1;
    public const uint PixelAlpha = 0x2;
    public const uint PixelFourCC = 0x4;
    public const uint PixelPaletteIndexed8 = 0x20;
    public const uint PixelRgb = 0x40;
    public const uint PixelYuv = 0x200;
    public const uint PixelLuminance = 0x20000;
    public const uint PixelBumpDuDv = 0x80000;

    // DDS_HEADER.dwCaps2
    public const uint Caps2CubeMap = 0x200;
    public const uint Caps2CubeMapAllFaces = 0xFC00;
    public const uint Caps2Volume = 0x200000;

    // DDS_HEADER_DXT10
    public const uint DimensionTexture1D = 2;
    public const uint DimensionTexture2D = 3;
    public const uint DimensionTexture3D = 4;
    public const uint MiscTextureCube = 0x4;
    public const uint AlphaModeMask = 0x7;

    /// <summary>Creates the value of a FourCC.</summary>
    public static uint FourCC(string code) => code[0] | ((uint)code[1] << 8) | ((uint)code[2] << 16) | ((uint)code[3] << 24);

    /// <summary>Determines whether data starts with the magic number.</summary>
    public static bool MatchesSignature(ReadOnlySpan<byte> prefix) => prefix.StartsWith(Magic);

    /// <summary>Describes a FourCC for a message: its four characters when they are printable, else its number.</summary>
    public static string DescribeFourCC(uint value)
    {
        Span<char> chars = stackalloc char[4];
        for (var i = 0; i < 4; i++)
        {
            var c = (char)((value >> (8 * i)) & 0xFF);
            if (c is < ' ' or > '~')
                return string.Create(CultureInfo.InvariantCulture, $"{value} (0x{value:X8})");

            chars[i] = c;
        }

        return "'" + new string(chars) + "'";
    }

    public static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.Dds);

    public static UnsupportedImageFeatureException Unsupported(string message, string feature) => new(message, ImageFormat.Dds, feature);
}
