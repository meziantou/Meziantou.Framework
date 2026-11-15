namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The <c>VkFormat</c> values of the KTX 2.0 decoder: the formats it reads, and the names of those it rejects.
/// </summary>
/// <remarks>
/// The values and names are those of the Vulkan specification (core 1.3 and the extensions KTX supports). The samples of
/// each format are the ones the Khronos Data Format Specification gives it, and that KTX-Software 4.4 writes: channel,
/// bit offset, bit length and qualifiers, in their order (the low bits first for the packed formats).
/// </remarks>
internal static class Ktx2VkFormats
{
    public const int Undefined = 0;

    private const int R = 1;
    private const int G = 2;
    private const int B = 4;
    private const int A = 8;

    // The channel ids of the ETC2 model (Khronos Data Format Specification, section 5.6.9)
    private const int Etc2ChannelRed = 0;
    private const int Etc2ChannelGreen = 1;
    private const int Etc2ChannelColor = 2;
    private const int Etc2ChannelAlpha = 15;

    private static readonly Dictionary<int, Ktx2VkFormat> Formats = CreateFormats();

    /// <summary>Gets the ETC1S format of the BasisLZ supercompression.</summary>
    public static Ktx2VkFormat Etc1S { get; } = new(Undefined, "UNDEFINED (ETC1S)", Ktx2Encoding.Etc1S, 4, 4, 8, 1, Ktx2Format.ModelEtc1S, isSrgb: false, Ktx2NumericType.Unorm, Ktx2OutputPrecision.Byte, isSigned: false, R | G | B | A, []);

    /// <summary>Gets the UASTC format.</summary>
    public static Ktx2VkFormat Uastc { get; } = new(Undefined, "UNDEFINED (UASTC)", Ktx2Encoding.Uastc, 4, 4, 16, 1, Ktx2Format.ModelUastc, isSrgb: false, Ktx2NumericType.Unorm, Ktx2OutputPrecision.Byte, isSigned: false, R | G | B | A, []);

    /// <summary>Gets the format of a <c>VkFormat</c> the decoder reads.</summary>
    public static bool TryGet(int value, [NotNullWhen(true)] out Ktx2VkFormat? format) => Formats.TryGetValue(value, out format);

    /// <summary>Gets the name of a <c>VkFormat</c>, for messages: the formats the decoder reads and the families it rejects.</summary>
    public static string GetName(int value)
    {
        if (Formats.TryGetValue(value, out var format))
            return format.Name;

        return value switch
        {
            0 => "UNDEFINED",
            >= 131 and <= 146 => "BC" + (value switch { <= 134 => "1", <= 136 => "2", <= 138 => "3", <= 140 => "4", <= 142 => "5", <= 144 => "6H", _ => "7" }) + " (" + value.ToString(CultureInfo.InvariantCulture) + ")",
            >= 1000054000 and <= 1000054007 => "PVRTC (" + value.ToString(CultureInfo.InvariantCulture) + ")",
            >= 1000156000 and <= 1000156033 => "YCbCr (" + value.ToString(CultureInfo.InvariantCulture) + ")",
            >= 1000330000 and <= 1000330003 => "YCbCr (" + value.ToString(CultureInfo.InvariantCulture) + ")",
            >= 1000288000 and <= 1000288029 => "ASTC 3D (" + value.ToString(CultureInfo.InvariantCulture) + ")",
            >= 124 and <= 130 => "depth/stencil (" + value.ToString(CultureInfo.InvariantCulture) + ")",
            _ => value.ToString(CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Gets the reason a <c>VkFormat</c> the decoder does not read is rejected, and the feature it names.</summary>
    public static (string Reason, string Feature) GetRejection(int value) => value switch
    {
        >= 1000054000 and <= 1000054007 => ("PVRTC blocks are not decoded.", "Format: PVRTC"),
        (>= 1000156000 and <= 1000156033) or (>= 1000330000 and <= 1000330003) => ("YCbCr formats (subsampled or multi-planar) are not decoded.", "Format: YCbCr"),
        >= 1000288000 and <= 1000288029 => ("3D ASTC blocks are not decoded.", "Format: ASTC 3D"),
        >= 124 and <= 130 => ("Depth and stencil formats are not images.", "Format: depth/stencil"),
        _ => ("The format is not one the decoder reads (signed, integer and 64-bit formats included).", "Format: " + GetName(value)),
    };

    private static Dictionary<int, Ktx2VkFormat> CreateFormats()
    {
        var formats = new Dictionary<int, Ktx2VkFormat>();

        void Packed(int value, string name, int bytes, Ktx2OutputPrecision precision, params (int Channel, int Bits)[] layout)
        {
            // The packed formats: the bit offsets grow from the low bits of the little-endian word
            var samples = new Ktx2Sample[layout.Length];
            var offset = 0;
            var mask = 0;
            for (var i = 0; i < layout.Length; i++)
            {
                samples[i] = new Ktx2Sample(layout[i].Channel, offset, layout[i].Bits, 0);
                offset += layout[i].Bits;
                mask |= ChannelBit(layout[i].Channel);
            }

            formats.Add(value, new Ktx2VkFormat(value, name, Ktx2Encoding.Uncompressed, 1, 1, bytes, bytes, Ktx2Format.ModelRgbsda, isSrgb: false, Ktx2NumericType.Unorm, precision, isSigned: false, mask, samples));
        }

        void Array(int value, string name, int componentBytes, Ktx2NumericType type, bool srgb, params int[] channels)
        {
            var qualifiers = type == Ktx2NumericType.Sfloat ? Ktx2Format.QualifierFloat | Ktx2Format.QualifierSigned : 0;
            var samples = new Ktx2Sample[channels.Length];
            var mask = 0;
            for (var i = 0; i < channels.Length; i++)
            {
                // The alpha of an sRGB format is linear
                var linear = srgb && channels[i] == Ktx2Format.ChannelAlpha ? Ktx2Format.QualifierLinear : 0;
                samples[i] = new Ktx2Sample(channels[i], i * componentBytes * 8, componentBytes * 8, qualifiers | linear);
                mask |= ChannelBit(channels[i]);
            }

            var precision = (type, componentBytes) switch
            {
                (Ktx2NumericType.Unorm, 1) => Ktx2OutputPrecision.Byte,
                (Ktx2NumericType.Unorm, 2) => Ktx2OutputPrecision.UInt16,
                (Ktx2NumericType.Sfloat, 2) => Ktx2OutputPrecision.Half,
                _ => Ktx2OutputPrecision.Float,
            };
            formats.Add(value, new Ktx2VkFormat(value, name, Ktx2Encoding.Uncompressed, 1, 1, componentBytes * channels.Length, componentBytes, Ktx2Format.ModelRgbsda, srgb, type, precision, isSigned: false, mask, samples));
        }

        const int Red = Ktx2Format.ChannelRed;
        const int Green = Ktx2Format.ChannelGreen;
        const int Blue = Ktx2Format.ChannelBlue;
        const int Alpha = Ktx2Format.ChannelAlpha;

        // The packed formats of 16 bits (the name lists the high bits first)
        Packed(2, "R4G4B4A4_UNORM_PACK16", 2, Ktx2OutputPrecision.Byte, (Alpha, 4), (Blue, 4), (Green, 4), (Red, 4));
        Packed(3, "B4G4R4A4_UNORM_PACK16", 2, Ktx2OutputPrecision.Byte, (Alpha, 4), (Red, 4), (Green, 4), (Blue, 4));
        Packed(4, "R5G6B5_UNORM_PACK16", 2, Ktx2OutputPrecision.Byte, (Blue, 5), (Green, 6), (Red, 5));
        Packed(5, "B5G6R5_UNORM_PACK16", 2, Ktx2OutputPrecision.Byte, (Red, 5), (Green, 6), (Blue, 5));
        Packed(6, "R5G5B5A1_UNORM_PACK16", 2, Ktx2OutputPrecision.Byte, (Alpha, 1), (Blue, 5), (Green, 5), (Red, 5));
        Packed(7, "B5G5R5A1_UNORM_PACK16", 2, Ktx2OutputPrecision.Byte, (Alpha, 1), (Red, 5), (Green, 5), (Blue, 5));
        Packed(8, "A1R5G5B5_UNORM_PACK16", 2, Ktx2OutputPrecision.Byte, (Blue, 5), (Green, 5), (Red, 5), (Alpha, 1));

        // The arrays of 8-bit components
        Array(9, "R8_UNORM", 1, Ktx2NumericType.Unorm, srgb: false, Red);
        Array(15, "R8_SRGB", 1, Ktx2NumericType.Unorm, srgb: true, Red);
        Array(16, "R8G8_UNORM", 1, Ktx2NumericType.Unorm, srgb: false, Red, Green);
        Array(22, "R8G8_SRGB", 1, Ktx2NumericType.Unorm, srgb: true, Red, Green);
        Array(23, "R8G8B8_UNORM", 1, Ktx2NumericType.Unorm, srgb: false, Red, Green, Blue);
        Array(29, "R8G8B8_SRGB", 1, Ktx2NumericType.Unorm, srgb: true, Red, Green, Blue);
        Array(30, "B8G8R8_UNORM", 1, Ktx2NumericType.Unorm, srgb: false, Blue, Green, Red);
        Array(36, "B8G8R8_SRGB", 1, Ktx2NumericType.Unorm, srgb: true, Blue, Green, Red);
        Array(37, "R8G8B8A8_UNORM", 1, Ktx2NumericType.Unorm, srgb: false, Red, Green, Blue, Alpha);
        Array(43, "R8G8B8A8_SRGB", 1, Ktx2NumericType.Unorm, srgb: true, Red, Green, Blue, Alpha);
        Array(44, "B8G8R8A8_UNORM", 1, Ktx2NumericType.Unorm, srgb: false, Blue, Green, Red, Alpha);
        Array(50, "B8G8R8A8_SRGB", 1, Ktx2NumericType.Unorm, srgb: true, Blue, Green, Red, Alpha);

        // A8B8G8R8_PACK32 is the same bytes as R8G8B8A8 (KTX section 3.2), but its typeSize is 4
        foreach (var (value, name, srgb) in new[] { (51, "A8B8G8R8_UNORM_PACK32", false), (57, "A8B8G8R8_SRGB_PACK32", true) })
        {
            var rgba = formats[srgb ? 43 : 37];
            formats.Add(value, new Ktx2VkFormat(value, name, Ktx2Encoding.Uncompressed, 1, 1, 4, 4, Ktx2Format.ModelRgbsda, srgb, Ktx2NumericType.Unorm, Ktx2OutputPrecision.Byte, isSigned: false, R | G | B | A, rgba.Samples));
        }

        // The packed formats of 32 bits with 10-bit components
        Packed(58, "A2R10G10B10_UNORM_PACK32", 4, Ktx2OutputPrecision.UInt16, (Blue, 10), (Green, 10), (Red, 10), (Alpha, 2));
        Packed(64, "A2B10G10R10_UNORM_PACK32", 4, Ktx2OutputPrecision.UInt16, (Red, 10), (Green, 10), (Blue, 10), (Alpha, 2));

        // The arrays of 16-bit and 32-bit components
        Array(70, "R16_UNORM", 2, Ktx2NumericType.Unorm, srgb: false, Red);
        Array(76, "R16_SFLOAT", 2, Ktx2NumericType.Sfloat, srgb: false, Red);
        Array(77, "R16G16_UNORM", 2, Ktx2NumericType.Unorm, srgb: false, Red, Green);
        Array(83, "R16G16_SFLOAT", 2, Ktx2NumericType.Sfloat, srgb: false, Red, Green);
        Array(84, "R16G16B16_UNORM", 2, Ktx2NumericType.Unorm, srgb: false, Red, Green, Blue);
        Array(90, "R16G16B16_SFLOAT", 2, Ktx2NumericType.Sfloat, srgb: false, Red, Green, Blue);
        Array(91, "R16G16B16A16_UNORM", 2, Ktx2NumericType.Unorm, srgb: false, Red, Green, Blue, Alpha);
        Array(97, "R16G16B16A16_SFLOAT", 2, Ktx2NumericType.Sfloat, srgb: false, Red, Green, Blue, Alpha);
        Array(100, "R32_SFLOAT", 4, Ktx2NumericType.Sfloat, srgb: false, Red);
        Array(103, "R32G32_SFLOAT", 4, Ktx2NumericType.Sfloat, srgb: false, Red, Green);
        Array(106, "R32G32B32_SFLOAT", 4, Ktx2NumericType.Sfloat, srgb: false, Red, Green, Blue);
        Array(109, "R32G32B32A32_SFLOAT", 4, Ktx2NumericType.Sfloat, srgb: false, Red, Green, Blue, Alpha);

        // The unsigned floats: 11, 11 and 10 bits, and a shared exponent (a mantissa sample and an exponent sample per color)
        formats.Add(122, new Ktx2VkFormat(122, "B10G11R11_UFLOAT_PACK32", Ktx2Encoding.Uncompressed, 1, 1, 4, 4, Ktx2Format.ModelRgbsda, isSrgb: false, Ktx2NumericType.Ufloat, Ktx2OutputPrecision.Half, isSigned: false, R | G | B,
            [new(Red, 0, 11, Ktx2Format.QualifierFloat), new(Green, 11, 11, Ktx2Format.QualifierFloat), new(Blue, 22, 10, Ktx2Format.QualifierFloat)]));
        formats.Add(123, new Ktx2VkFormat(123, "E5B9G9R9_UFLOAT_PACK32", Ktx2Encoding.Uncompressed, 1, 1, 4, 4, Ktx2Format.ModelRgbsda, isSrgb: false, Ktx2NumericType.SharedExponent, Ktx2OutputPrecision.Half, isSigned: false, R | G | B,
            [new(Red, 0, 9, 0), new(Red, 27, 5, Ktx2Format.QualifierExponent), new(Green, 9, 9, 0), new(Green, 27, 5, Ktx2Format.QualifierExponent), new(Blue, 18, 9, 0), new(Blue, 27, 5, Ktx2Format.QualifierExponent)]));

        // BCn (Khronos Data Format Specification, sections 18 to 20): one 64-bit sample per block, or an alpha (BC2, BC3)
        // or green (BC5) sample before the color or red one; the samples KTX-Software 4.4 writes
        void Bcn(int value, string name, int model, int bytes, bool srgb, Ktx2NumericType type, Ktx2OutputPrecision precision, bool signed, int mask, Ktx2Sample[] samples)
            => formats.Add(value, new Ktx2VkFormat(value, name, Ktx2Encoding.Bcn, 4, 4, bytes, 1, model, srgb, type, precision, signed, mask, samples));

        const int Signed = Ktx2Format.QualifierSigned;
        const int Float = Ktx2Format.QualifierFloat;
        const int Linear = Ktx2Format.QualifierLinear;
        Bcn(131, "BC1_RGB_UNORM_BLOCK", Ktx2Format.ModelBc1A, 8, srgb: false, Ktx2NumericType.Unorm, Ktx2OutputPrecision.Byte, signed: false, R | G | B, [new(0, 0, 64, 0)]);
        Bcn(132, "BC1_RGB_SRGB_BLOCK", Ktx2Format.ModelBc1A, 8, srgb: true, Ktx2NumericType.Unorm, Ktx2OutputPrecision.Byte, signed: false, R | G | B, [new(0, 0, 64, 0)]);
        Bcn(133, "BC1_RGBA_UNORM_BLOCK", Ktx2Format.ModelBc1A, 8, srgb: false, Ktx2NumericType.Unorm, Ktx2OutputPrecision.Byte, signed: false, R | G | B | A, [new(1, 0, 64, 0)]);
        Bcn(134, "BC1_RGBA_SRGB_BLOCK", Ktx2Format.ModelBc1A, 8, srgb: true, Ktx2NumericType.Unorm, Ktx2OutputPrecision.Byte, signed: false, R | G | B | A, [new(1, 0, 64, 0)]);
        Bcn(135, "BC2_UNORM_BLOCK", Ktx2Format.ModelBc2, 16, srgb: false, Ktx2NumericType.Unorm, Ktx2OutputPrecision.Byte, signed: false, R | G | B | A, [new(15, 0, 64, 0), new(0, 64, 64, 0)]);
        Bcn(136, "BC2_SRGB_BLOCK", Ktx2Format.ModelBc2, 16, srgb: true, Ktx2NumericType.Unorm, Ktx2OutputPrecision.Byte, signed: false, R | G | B | A, [new(15, 0, 64, Linear), new(0, 64, 64, 0)]);
        Bcn(137, "BC3_UNORM_BLOCK", Ktx2Format.ModelBc3, 16, srgb: false, Ktx2NumericType.Unorm, Ktx2OutputPrecision.Byte, signed: false, R | G | B | A, [new(15, 0, 64, 0), new(0, 64, 64, 0)]);
        Bcn(138, "BC3_SRGB_BLOCK", Ktx2Format.ModelBc3, 16, srgb: true, Ktx2NumericType.Unorm, Ktx2OutputPrecision.Byte, signed: false, R | G | B | A, [new(15, 0, 64, Linear), new(0, 64, 64, 0)]);
        Bcn(139, "BC4_UNORM_BLOCK", Ktx2Format.ModelBc4, 8, srgb: false, Ktx2NumericType.Unorm, Ktx2OutputPrecision.Byte, signed: false, R, [new(0, 0, 64, 0)]);
        Bcn(140, "BC4_SNORM_BLOCK", Ktx2Format.ModelBc4, 8, srgb: false, Ktx2NumericType.Unorm, Ktx2OutputPrecision.Float, signed: true, R, [new(0, 0, 64, Signed)]);
        Bcn(141, "BC5_UNORM_BLOCK", Ktx2Format.ModelBc5, 16, srgb: false, Ktx2NumericType.Unorm, Ktx2OutputPrecision.Byte, signed: false, R | G, [new(0, 0, 64, 0), new(1, 64, 64, 0)]);
        Bcn(142, "BC5_SNORM_BLOCK", Ktx2Format.ModelBc5, 16, srgb: false, Ktx2NumericType.Unorm, Ktx2OutputPrecision.Float, signed: true, R | G, [new(0, 0, 64, Signed), new(1, 64, 64, Signed)]);
        Bcn(143, "BC6H_UFLOAT_BLOCK", Ktx2Format.ModelBc6H, 16, srgb: false, Ktx2NumericType.Ufloat, Ktx2OutputPrecision.Half, signed: false, R | G | B, [new(0, 0, 128, Float)]);
        Bcn(144, "BC6H_SFLOAT_BLOCK", Ktx2Format.ModelBc6H, 16, srgb: false, Ktx2NumericType.Sfloat, Ktx2OutputPrecision.Half, signed: true, R | G | B, [new(0, 0, 128, Float | Signed)]);
        Bcn(145, "BC7_UNORM_BLOCK", Ktx2Format.ModelBc7, 16, srgb: false, Ktx2NumericType.Unorm, Ktx2OutputPrecision.Byte, signed: false, R | G | B | A, [new(0, 0, 128, 0)]);
        Bcn(146, "BC7_SRGB_BLOCK", Ktx2Format.ModelBc7, 16, srgb: true, Ktx2NumericType.Unorm, Ktx2OutputPrecision.Byte, signed: false, R | G | B | A, [new(0, 0, 128, 0)]);

        // ETC2 and EAC (Khronos Data Format Specification, section 22): the samples cover the 64-bit blocks of each channel
        void Block(int value, string name, Ktx2Encoding encoding, int bytes, bool srgb, bool signed, Ktx2OutputPrecision precision, int mask, Ktx2Sample[] samples)
            => formats.Add(value, new Ktx2VkFormat(value, name, encoding, 4, 4, bytes, 1, Ktx2Format.ModelEtc2, srgb, Ktx2NumericType.Unorm, precision, signed, mask, samples));

        Block(147, "ETC2_R8G8B8_UNORM_BLOCK", Ktx2Encoding.Etc2, 8, srgb: false, signed: false, Ktx2OutputPrecision.Byte, R | G | B, [new(Etc2ChannelColor, 0, 64, 0)]);
        Block(148, "ETC2_R8G8B8_SRGB_BLOCK", Ktx2Encoding.Etc2, 8, srgb: true, signed: false, Ktx2OutputPrecision.Byte, R | G | B, [new(Etc2ChannelColor, 0, 64, 0)]);
        Block(149, "ETC2_R8G8B8A1_UNORM_BLOCK", Ktx2Encoding.Etc2, 8, srgb: false, signed: false, Ktx2OutputPrecision.Byte, R | G | B | A, [new(Etc2ChannelColor, 0, 64, 0), new(Etc2ChannelAlpha, 0, 64, 0)]);
        Block(150, "ETC2_R8G8B8A1_SRGB_BLOCK", Ktx2Encoding.Etc2, 8, srgb: true, signed: false, Ktx2OutputPrecision.Byte, R | G | B | A, [new(Etc2ChannelColor, 0, 64, 0), new(Etc2ChannelAlpha, 0, 64, Ktx2Format.QualifierLinear)]);
        Block(151, "ETC2_R8G8B8A8_UNORM_BLOCK", Ktx2Encoding.Etc2, 16, srgb: false, signed: false, Ktx2OutputPrecision.Byte, R | G | B | A, [new(Etc2ChannelAlpha, 0, 64, 0), new(Etc2ChannelColor, 64, 64, 0)]);
        Block(152, "ETC2_R8G8B8A8_SRGB_BLOCK", Ktx2Encoding.Etc2, 16, srgb: true, signed: false, Ktx2OutputPrecision.Byte, R | G | B | A, [new(Etc2ChannelAlpha, 0, 64, Ktx2Format.QualifierLinear), new(Etc2ChannelColor, 64, 64, 0)]);
        Block(153, "EAC_R11_UNORM_BLOCK", Ktx2Encoding.Eac, 8, srgb: false, signed: false, Ktx2OutputPrecision.UInt16, R, [new(Etc2ChannelRed, 0, 64, 0)]);
        Block(154, "EAC_R11_SNORM_BLOCK", Ktx2Encoding.Eac, 8, srgb: false, signed: true, Ktx2OutputPrecision.Float, R, [new(Etc2ChannelRed, 0, 64, Ktx2Format.QualifierSigned)]);
        Block(155, "EAC_R11G11_UNORM_BLOCK", Ktx2Encoding.Eac, 16, srgb: false, signed: false, Ktx2OutputPrecision.UInt16, R | G, [new(Etc2ChannelRed, 0, 64, 0), new(Etc2ChannelGreen, 64, 64, 0)]);
        Block(156, "EAC_R11G11_SNORM_BLOCK", Ktx2Encoding.Eac, 16, srgb: false, signed: true, Ktx2OutputPrecision.Float, R | G, [new(Etc2ChannelRed, 0, 64, Ktx2Format.QualifierSigned), new(Etc2ChannelGreen, 64, 64, Ktx2Format.QualifierSigned)]);

        // ASTC (section 23): one 128-bit sample, signed float for the HDR formats
        (int Width, int Height)[] astcSizes = [(4, 4), (5, 4), (5, 5), (6, 5), (6, 6), (8, 5), (8, 6), (8, 8), (10, 5), (10, 6), (10, 8), (10, 10), (12, 10), (12, 12)];
        for (var i = 0; i < astcSizes.Length; i++)
        {
            var (width, height) = astcSizes[i];
            var size = string.Create(CultureInfo.InvariantCulture, $"{width}x{height}");
            formats.Add(157 + (2 * i), new Ktx2VkFormat(157 + (2 * i), "ASTC_" + size + "_UNORM_BLOCK", Ktx2Encoding.Astc, width, height, 16, 1, Ktx2Format.ModelAstc, isSrgb: false, Ktx2NumericType.Unorm, Ktx2OutputPrecision.Half, isSigned: false, R | G | B | A, [new(0, 0, 128, 0)]));
            formats.Add(158 + (2 * i), new Ktx2VkFormat(158 + (2 * i), "ASTC_" + size + "_SRGB_BLOCK", Ktx2Encoding.Astc, width, height, 16, 1, Ktx2Format.ModelAstc, isSrgb: true, Ktx2NumericType.Unorm, Ktx2OutputPrecision.Byte, isSigned: false, R | G | B | A, [new(0, 0, 128, 0)]));
            formats.Add(1000066000 + i, new Ktx2VkFormat(1000066000 + i, "ASTC_" + size + "_SFLOAT_BLOCK", Ktx2Encoding.Astc, width, height, 16, 1, Ktx2Format.ModelAstc, isSrgb: false, Ktx2NumericType.Sfloat, Ktx2OutputPrecision.Half, isSigned: false, R | G | B | A, [new(0, 0, 128, Ktx2Format.QualifierFloat | Ktx2Format.QualifierSigned)]));
        }

        return formats;
    }

    private static int ChannelBit(int channel) => channel switch
    {
        Ktx2Format.ChannelRed => R,
        Ktx2Format.ChannelGreen => G,
        Ktx2Format.ChannelBlue => B,
        Ktx2Format.ChannelAlpha => A,
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Not a color or alpha channel."),
    };
}
