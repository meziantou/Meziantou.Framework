using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// What a KTX 2.0 texture decodes to: the precision of its RGBA rows, the component mapping of <c>KTXswizzle</c> applied
/// to the channels of its format, its default pixel format, and its metadata.
/// </summary>
/// <remarks>
/// <para>
/// The texels of a format give the channels it has; the others are 0 for the colors and 1 for alpha (KTX section 5.4),
/// as a GPU samples them: an <c>R8</c> texture is red, and a luminance texture says so with the swizzle <c>rrr1</c>.
/// The swizzle then selects each output channel from them, or a constant. The output is gray when its three colors are
/// the same channel, and has alpha when its alpha is not the constant 1.
/// </para>
/// <para>
/// The channels of the Basis Universal formats are those their data format descriptor declares (KTX section 3.10.3):
/// an ETC1S texture of the slices RRR and GGG, or a UASTC texture of the channels RRRG or RG, holds a red and a green
/// channel; one of RRR holds a red channel.
/// </para>
/// </remarks>
internal sealed class Ktx2Output
{
    // The codes of a swizzle: a channel of the texels (0 to 3), or a constant
    public const int Zero = 4;
    public const int One = 5;

    private Ktx2Output(Ktx2OutputPrecision precision, int[] mapping, PixelFormat defaultPixelFormat, ImageColorModel colorModel, int bitsPerComponent, bool mayHaveTransparency)
    {
        Precision = precision;
        Mapping = mapping;
        DefaultPixelFormat = defaultPixelFormat;
        ColorModel = colorModel;
        BitsPerComponent = bitsPerComponent;
        MayHaveTransparency = mayHaveTransparency;
        IsIdentity = mapping is [0, 1, 2, 3];
    }

    public Ktx2OutputPrecision Precision { get; }

    /// <summary>Gets the source of each output channel: a texel channel (0 to 3), <see cref="Zero"/> or <see cref="One"/>.</summary>
    public int[] Mapping { get; }

    public bool IsIdentity { get; }

    public PixelFormat DefaultPixelFormat { get; }

    public ImageColorModel ColorModel { get; }

    public int BitsPerComponent { get; }

    public bool MayHaveTransparency { get; }

    /// <summary>Gets the pixel format of the rows the decoder writes.</summary>
    public PixelFormat SourcePixelFormat => Precision switch
    {
        Ktx2OutputPrecision.Byte => PixelFormat.Rgba32,
        Ktx2OutputPrecision.UInt16 => PixelFormat.Rgba64,
        Ktx2OutputPrecision.Half => PixelFormat.RgbaHalf,
        _ => PixelFormat.RgbaFloat,
    };

    /// <summary>Gets the length of one component of a row.</summary>
    public int ComponentLength => Precision switch
    {
        Ktx2OutputPrecision.Byte => 1,
        Ktx2OutputPrecision.UInt16 or Ktx2OutputPrecision.Half => 2,
        _ => 4,
    };

    public static Ktx2Output Create(Ktx2Texture texture)
    {
        var format = texture.Format ?? throw new InvalidOperationException("The format of the texture is not resolved.");
        var channels = GetChannelMask(texture);
        var swizzle = texture.Swizzle ?? "rgba";
        var mapping = new int[4];
        for (var i = 0; i < 4; i++)
        {
            var source = swizzle[i] switch
            {
                'r' => 0,
                'g' => 1,
                'b' => 2,
                'a' => 3,
                '0' => Zero,
                _ => One,
            };

            // A channel the texels lack is 0 for the colors and 1 for alpha
            if (source < 4 && (channels & (1 << source)) == 0)
            {
                source = source == 3 ? One : Zero;
            }
            else if (source == 1 && StoresGreenInAlpha(texture))
            {
                source = 3;
            }

            mapping[i] = source;
        }

        var precision = GetPrecision(texture);
        var isGray = mapping[0] == mapping[1] && mapping[1] == mapping[2];
        var hasAlpha = mapping[3] != One;
        PixelFormat defaultFormat;
        int bits;
        if (precision is Ktx2OutputPrecision.Half or Ktx2OutputPrecision.Float)
        {
            // A gray texture (a single channel swizzled to the three colors) keeps its gray level, a color one its alpha
            defaultFormat = DefaultPixelFormats.ForKtx2(isGray, hasAlpha, precision);
            bits = precision == Ktx2OutputPrecision.Half ? 16 : 32;
            hasAlpha |= !isGray;
        }
        else
        {
            var deep = precision == Ktx2OutputPrecision.UInt16;
            defaultFormat = DefaultPixelFormats.ForKtx2(isGray, hasAlpha, precision);
            bits = GetBitsPerComponent(format, deep);
        }

        var colorModel = (isGray, hasAlpha) switch
        {
            (true, false) => ImageColorModel.Grayscale,
            (true, true) => ImageColorModel.GrayscaleAlpha,
            (false, false) => ImageColorModel.Rgb,
            _ => ImageColorModel.Rgba,
        };
        return new Ktx2Output(precision, mapping, defaultFormat, colorModel, bits, hasAlpha);
    }

    /// <summary>
    /// Gets the precision of the texels: the one of the format, except for the linear LDR ASTC formats, which decode to
    /// halves (decode_float16, KTX section 5.7) unless <c>KTXastcDecodeMode</c> is <c>unorm8</c>.
    /// </summary>
    public static Ktx2OutputPrecision GetPrecision(Ktx2Texture texture)
    {
        var format = texture.Format!;
        return format.Encoding == Ktx2Encoding.Astc && !format.IsSrgb && format.NumericType == Ktx2NumericType.Unorm && texture.AstcDecodeMode == "unorm8"
            ? Ktx2OutputPrecision.Byte
            : format.Precision;
    }

    /// <summary>Writes the color labels of the data format descriptor and the reported metadata of the texture.</summary>
    /// <exception cref="UnsupportedImageFeatureException">The color description has no label in the library.</exception>
    public static ImageMetadata CreateMetadata(Ktx2Texture texture)
    {
        var format = texture.Format!;
        var metadata = new ImageMetadata { SourceFormat = ImageFormat.Ktx2 };
        var primaries = texture.ColorPrimaries;
        var transfer = texture.TransferFunction;
        if (GetPrecision(texture) is Ktx2OutputPrecision.Half or Ktx2OutputPrecision.Float)
        {
            // Floating-point and signed samples are linear values
            if (transfer is not (Ktx2Format.TransferLinear or Ktx2Format.TransferUnspecified) || primaries is not (Ktx2Format.PrimariesBt709 or Ktx2Format.PrimariesUnspecified))
            {
                SetCicp(metadata, primaries, transfer, floatingPoint: true);
            }
            else
            {
                metadata.TransferFunction = ColorTransferFunction.Linear;
            }
        }
        else if (primaries is Ktx2Format.PrimariesBt709 or Ktx2Format.PrimariesUnspecified && transfer is Ktx2Format.TransferSrgb or Ktx2Format.TransferLinear or Ktx2Format.TransferUnspecified)
        {
            // Without a transfer function, the samples are untagged: assumed sRGB, as the other formats
            metadata.TransferFunction = transfer == Ktx2Format.TransferLinear ? ColorTransferFunction.Linear : ColorTransferFunction.Srgb;
        }
        else
        {
            SetCicp(metadata, primaries, transfer, floatingPoint: false);
        }

        // Section 5.2: the first texel is the origin of the texture coordinates; T growing upward puts the first row at the bottom
        if (texture.Orientation is { } orientation && orientation.Length >= 2)
        {
            metadata.Orientation = (orientation[0], orientation[1]) switch
            {
                ('l', 'd') => ExifOrientation.TopRight,
                ('l', 'u') => ExifOrientation.BottomRight,
                ('r', 'u') => ExifOrientation.BottomLeft,
                _ => ExifOrientation.TopLeft,
            };
        }
        else if (texture.Orientation is "l")
        {
            metadata.Orientation = ExifOrientation.TopRight;
        }

        metadata.Ktx2Header = new Ktx2Header(texture.VkFormat, format.Name, (int)texture.Scheme, texture.PixelWidth, texture.PixelHeight, texture.PixelDepth, texture.LayerCount, texture.FaceCount, texture.LevelCount, texture.ColorModel, primaries, transfer, texture.KeyValues);
        return metadata;
    }

    private static void SetCicp(ImageMetadata metadata, int primaries, int transfer, bool floatingPoint)
    {
        // ITU-T H.273 code points of the primaries and the transfer functions of the Khronos Data Format Specification
        var colorPrimaries = primaries switch
        {
            Ktx2Format.PrimariesUnspecified => 2,
            Ktx2Format.PrimariesBt709 => 1,
            Ktx2Format.PrimariesBt601Ebu => 5,
            Ktx2Format.PrimariesBt601Smpte => 6,
            Ktx2Format.PrimariesBt2020 => 9,
            Ktx2Format.PrimariesCieXyz => 10,
            Ktx2Format.PrimariesNtsc1953 => 4,
            Ktx2Format.PrimariesDisplayP3 => 12,
            _ => -1,
        };
        var transferCharacteristics = transfer switch
        {
            Ktx2Format.TransferUnspecified or Ktx2Format.TransferLinear => 8,
            Ktx2Format.TransferSrgb => 13,
            Ktx2Format.TransferItu => 1,
            Ktx2Format.TransferHlgOetf => 18,
            Ktx2Format.TransferPqEotf => 16,
            _ => -1,
        };
        if (colorPrimaries < 0 || transferCharacteristics < 0 || (floatingPoint && transferCharacteristics != 8))
            throw Ktx2Format.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The KTX 2.0 texture has the color primaries {primaries} and the transfer function {transfer}{(floatingPoint ? " for floating-point or signed samples" : "")}, which the library cannot label."), "Color description");

        metadata.Cicp = new CicpColorDescription((byte)colorPrimaries, (byte)transferCharacteristics, matrixCoefficients: 0, fullRange: true);
    }

    /// <summary>
    /// Determines whether the green channel of the texels is in the alpha of the decoded blocks: the UASTC channels RRRG,
    /// and the second ETC1S slice of the channels RRR and GGG, which the ETC1S decoder writes to alpha.
    /// </summary>
    private static bool StoresGreenInAlpha(Ktx2Texture texture) => texture.Format!.Encoding switch
    {
        Ktx2Encoding.Etc1S => texture.BasisSecondChannelId == Ktx2Format.Etc1SChannelGgg,
        Ktx2Encoding.Uastc => texture.BasisChannelId == Ktx2Format.UastcChannelRrrg,
        _ => false,
    };

    private static int GetChannelMask(Ktx2Texture texture)
    {
        const int Red = 1;
        const int Green = 2;
        const int All = 15;
        return texture.Format!.Encoding switch
        {
            Ktx2Encoding.Etc1S => (texture.BasisChannelId, texture.BasisSecondChannelId) switch
            {
                (Ktx2Format.Etc1SChannelRrr, Ktx2Format.Etc1SChannelGgg) => Red | Green,
                (Ktx2Format.Etc1SChannelRrr, _) => Red,
                (_, Ktx2Format.Etc1SChannelAaa) => All,
                _ => 7,
            },
            Ktx2Encoding.Uastc => texture.BasisChannelId switch
            {
                Ktx2Format.UastcChannelRgba => All,
                Ktx2Format.UastcChannelRrr => Red,
                Ktx2Format.UastcChannelRrrg or Ktx2Format.UastcChannelRg => Red | Green,
                _ => 7,
            },
            _ => texture.Format.ChannelMask,
        };
    }

    private static int GetBitsPerComponent(Ktx2VkFormat format, bool deep)
    {
        if (format.Encoding != Ktx2Encoding.Uncompressed)
            return deep ? 16 : 8;

        var bits = 0;
        foreach (var sample in format.Samples)
        {
            bits = Math.Max(bits, sample.BitLength);
        }

        return bits;
    }
}
