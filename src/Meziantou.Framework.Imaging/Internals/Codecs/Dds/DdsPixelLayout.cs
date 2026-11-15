using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The pixel format of a DDS file, resolved from its <c>DDS_PIXELFORMAT</c> (FourCC, legacy masks) or the
/// <c>dxgiFormat</c> of its DX10 header, and the representation it is decoded to.
/// </summary>
/// <remarks>
/// <para>
/// Every format decodes to a lossless representation of what it stores. Masks of up to 8 bits are expanded to 8-bit samples
/// (<c>round(value × 255 / max)</c>), a layout with a wider mask to 16-bit samples (<c>round(value × 65535 / max)</c>), as
/// BMP bit fields are. A single channel (red, luminance) is gray, two channels (red and green) are RGB with a blue of 0,
/// and a missing alpha is opaque, as the Khronos Data Format Specification gives the RGBA value of these formats.
/// Floating-point formats and BC6H decode to <see cref="PixelFormat.RgbaHalf"/> when every value is a half
/// (R11G11B10_FLOAT and R9G9B9E5_SHAREDEXP are), otherwise to <see cref="PixelFormat.RgbaFloat"/>, and the formats of
/// one channel to <see cref="PixelFormat.GrayHalf"/> or <see cref="PixelFormat.GrayFloat"/>; the signed BC4 and BC5
/// formats to <see cref="PixelFormat.GrayFloat"/> and <see cref="PixelFormat.RgbaFloat"/> values in [-1, 1].
/// </para>
/// <para>
/// The masks are read as they are written. Some writers (D3DX among them) swap the red and blue masks of the 10:10:10:2
/// layouts, and DirectXTex reads such legacy headers swapped back; the library does not guess.
/// </para>
/// </remarks>
internal sealed class DdsPixelLayout
{
    private DdsPixelLayout(string name, DdsEncoding encoding, int channels, bool isGray, bool hasAlpha, bool isSrgb, int bytesPerPixel = 0)
    {
        Name = name;
        Encoding = encoding;
        Channels = channels;
        IsGray = isGray;
        HasAlpha = hasAlpha;
        IsSrgb = isSrgb;
        BytesPerPixel = bytesPerPixel;
    }

    /// <summary>Gets the name of the format, for messages: a DXGI name, a FourCC or a description of the masks.</summary>
    public string Name { get; }

    public DdsEncoding Encoding { get; }

    /// <summary>Gets the number of stored channels of a floating-point format (1 to 4), or of the channels the masks give.</summary>
    public int Channels { get; }

    /// <summary>Gets a value indicating whether the colors are one channel (red or luminance), decoded as gray.</summary>
    public bool IsGray { get; }

    /// <summary>Gets a value indicating whether the format stores alpha.</summary>
    public bool HasAlpha { get; }

    /// <summary>Gets a value indicating whether the colors are sRGB encoded (an <c>_SRGB</c> DXGI format).</summary>
    public bool IsSrgb { get; }

    /// <summary>Gets the number of bytes of an uncompressed pixel, or 0 for a block-compressed format.</summary>
    public int BytesPerPixel { get; }

    /// <summary>Gets the masks of a <see cref="DdsEncoding.Masks"/> format: red (or luminance), green, blue, alpha.</summary>
    public uint RedMask { get; private init; }

    public uint GreenMask { get; private init; }

    public uint BlueMask { get; private init; }

    public uint AlphaMask { get; private init; }

    /// <summary>Gets a value indicating whether the texels are stored in blocks of 4×4.</summary>
    public bool IsBlockCompressed => Encoding is <= DdsEncoding.Bc7;

    /// <summary>Gets the number of bytes of a block of a block-compressed format.</summary>
    public int BlockLength => Encoding is DdsEncoding.Bc1 or DdsEncoding.Bc4 or DdsEncoding.Bc4Signed ? BlockDecoder.SmallBlockLength : BlockDecoder.LargeBlockLength;

    /// <summary>Gets a value indicating whether a mask is wider than 8 bits: the samples are then 16-bit.</summary>
    public bool IsWide => Encoding switch
    {
        DdsEncoding.Masks => MaxMaskBits > 8,
        DdsEncoding.Rgba16 => true,
        _ => false,
    };

    /// <summary>Gets the number of bits of the widest mask.</summary>
    public int MaxMaskBits => Math.Max(Math.Max(BitOperations.PopCount(RedMask), BitOperations.PopCount(GreenMask)), Math.Max(BitOperations.PopCount(BlueMask), BitOperations.PopCount(AlphaMask)));

    /// <summary>Gets a value indicating whether the pixels are floating-point values (or signed normalized values, decoded to floats).</summary>
    public bool IsFloatingPoint => Encoding is DdsEncoding.Bc6H or DdsEncoding.Bc6HSigned or DdsEncoding.Bc4Signed or DdsEncoding.Bc5Signed or DdsEncoding.Half or DdsEncoding.Float or DdsEncoding.R11G11B10Float or DdsEncoding.R9G9B9E5;

    /// <summary>Gets a value indicating whether the format has a color (it is not an alpha mask).</summary>
    public bool HasColor => !(Encoding == DdsEncoding.Masks && RedMask == 0 && GreenMask == 0 && BlueMask == 0);

    /// <summary>Gets the default pixel format of the decoded images.</summary>
    public PixelFormat DefaultPixelFormat
    {
        get
        {
            PixelFormat? floatingPoint = Encoding switch
            {
                DdsEncoding.Bc6H or DdsEncoding.Bc6HSigned or DdsEncoding.Half or DdsEncoding.R11G11B10Float or DdsEncoding.R9G9B9E5 => PixelFormat.RgbaHalf,
                DdsEncoding.Float or DdsEncoding.Bc4Signed or DdsEncoding.Bc5Signed => PixelFormat.RgbaFloat,
                _ => null,
            };

            return DefaultPixelFormats.ForDds(floatingPoint, HasColor, IsGray, HasAlpha, IsWide);
        }
    }

    /// <summary>Gets the layout of the rows the decoder produces: the widened form of <see cref="DefaultPixelFormat"/>.</summary>
    public PixelFormat SourcePixelFormat => PixelFormatWidening.GetWidenedFormat(DefaultPixelFormat);

    /// <summary>Gets the color model of the encoded samples.</summary>
    public ImageColorModel ColorModel
    {
        get
        {
            if (!HasColor || IsGray)
                return HasAlpha ? ImageColorModel.GrayscaleAlpha : ImageColorModel.Grayscale;

            return HasAlpha ? ImageColorModel.Rgba : ImageColorModel.Rgb;
        }
    }

    /// <summary>Gets the precision of a decoded component: 8, 16 or 32 bits.</summary>
    public int BitsPerComponent => DefaultPixelFormat switch
    {
        PixelFormat.RgbaFloat or PixelFormat.GrayFloat => 32,
        PixelFormat.RgbaHalf or PixelFormat.GrayHalf => 16,
        _ => IsWide ? 16 : 8,
    };

    /// <summary>Resolves the format of a <c>DDS_PIXELFORMAT</c> without a DX10 header.</summary>
    /// <param name="flags">The <c>dwFlags</c> of the pixel format.</param>
    /// <param name="fourCC">The FourCC.</param>
    /// <param name="bitCount">The <c>dwRGBBitCount</c>.</param>
    /// <param name="redMask">The red (or luminance) mask.</param>
    /// <param name="greenMask">The green mask.</param>
    /// <param name="blueMask">The blue mask.</param>
    /// <param name="alphaMask">The alpha mask.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="InvalidImageContentException">The pixel format is inconsistent.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The pixel format is valid but not supported.</exception>
    public static DdsPixelLayout FromLegacy(uint flags, uint fourCC, uint bitCount, uint redMask, uint greenMask, uint blueMask, uint alphaMask)
    {
        if ((flags & DdsFormat.PixelFourCC) != 0)
            return FromFourCC(fourCC);

        var hasAlpha = (flags & DdsFormat.PixelAlphaPixels) != 0;
        if ((flags & DdsFormat.PixelRgb) != 0)
            return FromMasks("legacy RGB masks", bitCount, redMask, greenMask, blueMask, hasAlpha ? alphaMask : 0, isGray: false, isSrgb: false);

        if ((flags & DdsFormat.PixelLuminance) != 0)
            return FromMasks("legacy luminance masks", bitCount, redMask, 0, 0, hasAlpha ? alphaMask : 0, isGray: true, isSrgb: false);

        if ((flags & DdsFormat.PixelAlpha) != 0)
        {
            if (BitOperations.PopCount(alphaMask) > 8)
                throw DdsFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The DDS alpha-only pixel format has an alpha mask of {BitOperations.PopCount(alphaMask)} bits; alpha masks of more than 8 bits are not supported."), "Alpha-only format wider than 8 bits");

            return FromMasks("legacy alpha mask", bitCount, 0, 0, 0, alphaMask, isGray: false, isSrgb: false);
        }

        if ((flags & DdsFormat.PixelYuv) != 0)
            throw DdsFormat.Unsupported("The DDS pixel format stores YUV samples (DDPF_YUV): video formats are not supported.", "YUV pixel format");

        if ((flags & DdsFormat.PixelBumpDuDv) != 0)
            throw DdsFormat.Unsupported("The DDS pixel format stores signed bump-map offsets (DDPF_BUMPDUDV), which are not supported.", "Bump-map pixel format");

        if ((flags & DdsFormat.PixelPaletteIndexed8) != 0)
            throw DdsFormat.Unsupported("The DDS pixel format stores palette indexes (DDPF_PALETTEINDEXED8), which are not supported.", "Palette pixel format");

        throw DdsFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DDS pixel format flags 0x{flags:X8} describe no pixel format (no DDPF_FOURCC, DDPF_RGB, DDPF_LUMINANCE or DDPF_ALPHA)."));
    }

    /// <summary>Resolves a <c>DXGI_FORMAT</c> of a DX10 header.</summary>
    /// <param name="format">The DXGI format.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="InvalidImageContentException">The value is <c>DXGI_FORMAT_UNKNOWN</c>.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The format is not supported.</exception>
    public static DdsPixelLayout FromDxgi(uint format) => format switch
    {
        2 => Floats("R32G32B32A32_FLOAT", DdsEncoding.Float, 4),
        6 => Floats("R32G32B32_FLOAT", DdsEncoding.Float, 3),
        10 => Floats("R16G16B16A16_FLOAT", DdsEncoding.Half, 4),
        11 => new("R16G16B16A16_UNORM", DdsEncoding.Rgba16, 4, isGray: false, hasAlpha: true, isSrgb: false, bytesPerPixel: 8),
        16 => Floats("R32G32_FLOAT", DdsEncoding.Float, 2),
        24 => FromMasks("R10G10B10A2_UNORM", 32, 0x3FF, 0xFFC00, 0x3FF00000, 0xC0000000, isGray: false, isSrgb: false),
        26 => new("R11G11B10_FLOAT", DdsEncoding.R11G11B10Float, 3, isGray: false, hasAlpha: false, isSrgb: false, bytesPerPixel: 4),
        27 => Rgba8("R8G8B8A8_TYPELESS", isSrgb: false),
        28 => Rgba8("R8G8B8A8_UNORM", isSrgb: false),
        29 => Rgba8("R8G8B8A8_UNORM_SRGB", isSrgb: true),
        34 => Floats("R16G16_FLOAT", DdsEncoding.Half, 2),
        35 => FromMasks("R16G16_UNORM", 32, 0xFFFF, 0xFFFF0000, 0, 0, isGray: false, isSrgb: false),
        41 => Floats("R32_FLOAT", DdsEncoding.Float, 1),
        49 => FromMasks("R8G8_UNORM", 16, 0xFF, 0xFF00, 0, 0, isGray: false, isSrgb: false),
        54 => Floats("R16_FLOAT", DdsEncoding.Half, 1),
        56 => FromMasks("R16_UNORM", 16, 0xFFFF, 0, 0, 0, isGray: true, isSrgb: false),
        61 => FromMasks("R8_UNORM", 8, 0xFF, 0, 0, 0, isGray: true, isSrgb: false),
        65 => FromMasks("A8_UNORM", 8, 0, 0, 0, 0xFF, isGray: false, isSrgb: false),
        67 => new("R9G9B9E5_SHAREDEXP", DdsEncoding.R9G9B9E5, 3, isGray: false, hasAlpha: false, isSrgb: false, bytesPerPixel: 4),
        70 => Block("BC1_TYPELESS", DdsEncoding.Bc1, isSrgb: false),
        71 => Block("BC1_UNORM", DdsEncoding.Bc1, isSrgb: false),
        72 => Block("BC1_UNORM_SRGB", DdsEncoding.Bc1, isSrgb: true),
        73 => Block("BC2_TYPELESS", DdsEncoding.Bc2, isSrgb: false),
        74 => Block("BC2_UNORM", DdsEncoding.Bc2, isSrgb: false),
        75 => Block("BC2_UNORM_SRGB", DdsEncoding.Bc2, isSrgb: true),
        76 => Block("BC3_TYPELESS", DdsEncoding.Bc3, isSrgb: false),
        77 => Block("BC3_UNORM", DdsEncoding.Bc3, isSrgb: false),
        78 => Block("BC3_UNORM_SRGB", DdsEncoding.Bc3, isSrgb: true),
        79 => Block("BC4_TYPELESS", DdsEncoding.Bc4, isSrgb: false),
        80 => Block("BC4_UNORM", DdsEncoding.Bc4, isSrgb: false),
        81 => Block("BC4_SNORM", DdsEncoding.Bc4Signed, isSrgb: false),
        82 => Block("BC5_TYPELESS", DdsEncoding.Bc5, isSrgb: false),
        83 => Block("BC5_UNORM", DdsEncoding.Bc5, isSrgb: false),
        84 => Block("BC5_SNORM", DdsEncoding.Bc5Signed, isSrgb: false),
        85 => FromMasks("B5G6R5_UNORM", 16, 0xF800, 0x7E0, 0x1F, 0, isGray: false, isSrgb: false),
        86 => FromMasks("B5G5R5A1_UNORM", 16, 0x7C00, 0x3E0, 0x1F, 0x8000, isGray: false, isSrgb: false),
        87 => Bgra8("B8G8R8A8_UNORM", alpha: true, isSrgb: false),
        88 => Bgra8("B8G8R8X8_UNORM", alpha: false, isSrgb: false),
        90 => Bgra8("B8G8R8A8_TYPELESS", alpha: true, isSrgb: false),
        91 => Bgra8("B8G8R8A8_UNORM_SRGB", alpha: true, isSrgb: true),
        92 => Bgra8("B8G8R8X8_TYPELESS", alpha: false, isSrgb: false),
        93 => Bgra8("B8G8R8X8_UNORM_SRGB", alpha: false, isSrgb: true),
        95 => Block("BC6H_UF16", DdsEncoding.Bc6H, isSrgb: false),
        96 => Block("BC6H_SF16", DdsEncoding.Bc6HSigned, isSrgb: false),
        97 => Block("BC7_TYPELESS", DdsEncoding.Bc7, isSrgb: false),
        98 => Block("BC7_UNORM", DdsEncoding.Bc7, isSrgb: false),
        99 => Block("BC7_UNORM_SRGB", DdsEncoding.Bc7, isSrgb: true),
        115 => FromMasks("B4G4R4A4_UNORM", 16, 0xF00, 0xF0, 0xF, 0xF000, isGray: false, isSrgb: false),
        0 => throw DdsFormat.Invalid("The DX10 header of the DDS file has the format DXGI_FORMAT_UNKNOWN (0)."),
        _ => throw UnsupportedDxgi(format),
    };

    private static DdsPixelLayout FromFourCC(uint fourCC)
    {
        switch (fourCC)
        {
            case 36:
                return new("D3DFMT_A16B16G16R16", DdsEncoding.Rgba16, 4, isGray: false, hasAlpha: true, isSrgb: false, bytesPerPixel: 8);
            case 111:
                return Floats("D3DFMT_R16F", DdsEncoding.Half, 1);
            case 112:
                return Floats("D3DFMT_G16R16F", DdsEncoding.Half, 2);
            case 113:
                return Floats("D3DFMT_A16B16G16R16F", DdsEncoding.Half, 4);
            case 114:
                return Floats("D3DFMT_R32F", DdsEncoding.Float, 1);
            case 115:
                return Floats("D3DFMT_G32R32F", DdsEncoding.Float, 2);
            case 116:
                return Floats("D3DFMT_A32B32G32R32F", DdsEncoding.Float, 4);
            case 110:
                throw DdsFormat.Unsupported("The DDS pixel format is D3DFMT_Q16W16V16U16 (FourCC 110), signed 16-bit samples, which are not supported.", "Signed normalized format");
            case 117:
                throw DdsFormat.Unsupported("The DDS pixel format is D3DFMT_CxV8U8 (FourCC 117), a signed two-channel format, which is not supported.", "Signed normalized format");
        }

        var name = DdsFormat.DescribeFourCC(fourCC);
        if (fourCC == DdsFormat.FourCC("DXT1"))
            return Block("DXT1", DdsEncoding.Bc1, isSrgb: false);

        if (fourCC == DdsFormat.FourCC("DXT3"))
            return Block("DXT3", DdsEncoding.Bc2, isSrgb: false);

        if (fourCC == DdsFormat.FourCC("DXT5"))
            return Block("DXT5", DdsEncoding.Bc3, isSrgb: false);

        if (fourCC == DdsFormat.FourCC("DXT2") || fourCC == DdsFormat.FourCC("DXT4"))
            throw DdsFormat.Unsupported($"The DDS pixel format {name} stores colors premultiplied by alpha; the working pixel formats store straight alpha and the library never divides it out.", "Premultiplied alpha");

        if (fourCC == DdsFormat.FourCC("ATI1") || fourCC == DdsFormat.FourCC("BC4U"))
            return Block(name, DdsEncoding.Bc4, isSrgb: false);

        if (fourCC == DdsFormat.FourCC("BC4S"))
            return Block(name, DdsEncoding.Bc4Signed, isSrgb: false);

        if (fourCC == DdsFormat.FourCC("ATI2") || fourCC == DdsFormat.FourCC("BC5U"))
            return Block(name, DdsEncoding.Bc5, isSrgb: false);

        if (fourCC == DdsFormat.FourCC("BC5S"))
            return Block(name, DdsEncoding.Bc5Signed, isSrgb: false);

        if (fourCC == DdsFormat.FourCC("YUY2") || fourCC == DdsFormat.FourCC("UYVY") || fourCC == DdsFormat.FourCC("RGBG") || fourCC == DdsFormat.FourCC("GRGB"))
            throw DdsFormat.Unsupported($"The DDS pixel format {name} is a packed format of shared samples (video formats and R8G8_B8G8), which is not supported.", "Video format");

        throw DdsFormat.Unsupported($"The DDS pixel format FourCC {name} is not supported.", "FourCC " + name);
    }

    private static DdsPixelLayout Floats(string name, DdsEncoding encoding, int channels)
        => new(name, encoding, channels, isGray: channels == 1, hasAlpha: channels == 4, isSrgb: false, bytesPerPixel: channels * (encoding == DdsEncoding.Half ? 2 : 4));

    private static DdsPixelLayout Block(string name, DdsEncoding encoding, bool isSrgb) => encoding switch
    {
        DdsEncoding.Bc1 or DdsEncoding.Bc2 or DdsEncoding.Bc3 or DdsEncoding.Bc7 => new(name, encoding, 4, isGray: false, hasAlpha: true, isSrgb),
        DdsEncoding.Bc4 or DdsEncoding.Bc4Signed => new(name, encoding, 1, isGray: true, hasAlpha: false, isSrgb),
        DdsEncoding.Bc5 or DdsEncoding.Bc5Signed => new(name, encoding, 2, isGray: false, hasAlpha: false, isSrgb),
        DdsEncoding.Bc6H or DdsEncoding.Bc6HSigned => new(name, encoding, 3, isGray: false, hasAlpha: false, isSrgb),
        _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "The encoding is not block-compressed."),
    };

    private static DdsPixelLayout Rgba8(string name, bool isSrgb) => FromMasks(name, 32, 0xFF, 0xFF00, 0xFF0000, 0xFF000000, isGray: false, isSrgb);

    private static DdsPixelLayout Bgra8(string name, bool alpha, bool isSrgb) => FromMasks(name, 32, 0xFF0000, 0xFF00, 0xFF, alpha ? 0xFF000000 : 0, isGray: false, isSrgb);

    private static DdsPixelLayout FromMasks(string name, uint bitCount, uint red, uint green, uint blue, uint alpha, bool isGray, bool isSrgb)
    {
        if (bitCount is not (8 or 16 or 24 or 32))
            throw DdsFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DDS pixel format ({name}) has {bitCount} bits per pixel; 8, 16, 24 or 32 bits are expected."));

        var limit = bitCount == 32 ? uint.MaxValue : (1u << (int)bitCount) - 1;
        uint used = 0;
        foreach (var (mask, channel) in new[] { (red, isGray ? "luminance" : "red"), (green, "green"), (blue, "blue"), (alpha, "alpha") })
        {
            if (mask == 0)
                continue;

            var shift = BitOperations.TrailingZeroCount(mask);
            var bits = BitOperations.PopCount(mask);
            if ((mask >> shift) != (bits == 32 ? uint.MaxValue : (1u << bits) - 1))
                throw DdsFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DDS {channel} mask 0x{mask:X8} ({name}) is not a contiguous run of bits."));

            if ((mask & ~limit) != 0)
                throw DdsFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DDS {channel} mask 0x{mask:X8} ({name}) does not fit in a {bitCount}-bit pixel."));

            if ((mask & used) != 0)
                throw DdsFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The DDS {channel} mask 0x{mask:X8} ({name}) overlaps another mask."));

            if (bits > 16)
                throw DdsFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The DDS {channel} mask 0x{mask:X8} ({name}) has {bits} bits; channels of more than 16 bits are not supported."), "Channel wider than 16 bits");

            used |= mask;
        }

        if (used == 0)
            throw DdsFormat.Invalid($"The DDS pixel format ({name}) has no channel: every mask is 0.");

        var channels = (red != 0 ? 1 : 0) + (green != 0 ? 1 : 0) + (blue != 0 ? 1 : 0) + (alpha != 0 ? 1 : 0);
        return new DdsPixelLayout(name, DdsEncoding.Masks, channels, isGray && red != 0, alpha != 0, isSrgb, (int)(bitCount / 8))
        {
            RedMask = red,
            GreenMask = green,
            BlueMask = blue,
            AlphaMask = alpha,
        };
    }

    private static UnsupportedImageFeatureException UnsupportedDxgi(uint format)
    {
        var kind = format switch
        {
            19 or 20 or 21 or 22 or 39 or 40 or 44 or 45 or 46 or 47 or 55 => "a depth or stencil format",
            >= 100 and <= 118 or 130 or 131 or 132 => "a video or palette format",
            1 or 5 or 9 or 15 or 18 or 23 or 33 or 38 or 48 or 53 or 60 or 94 => "a typeless format without a reasonable interpretation",
            _ => "not supported",
        };

        return DdsFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The DXGI format {format} of the DDS file is {kind}."), string.Create(CultureInfo.InvariantCulture, $"DXGI format {format}"));
    }
}
