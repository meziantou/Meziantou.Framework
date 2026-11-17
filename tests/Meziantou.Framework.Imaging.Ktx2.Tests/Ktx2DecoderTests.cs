using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Ktx2;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// KTX 2.0 decoding of files assembled by <see cref="Ktx2FileBuilder"/> (KTX File Format Specification 2.0; Khronos Data
/// Format Specification 1.4): the container, the data format descriptor, the key/value data, the supercompression, the
/// uncompressed formats, blocks of ETC2, EAC, ASTC and UASTC built bit by bit, the subresources, the animations, the
/// errors and the limits. The expected values are hand-computed from the specifications.
/// </summary>
public sealed class Ktx2DecoderTests
{
    private const uint R8G8B8A8Unorm = 37;
    private const uint R8G8B8A8Srgb = 43;

    // 3x2 RGBA pixels, every one different
    private static readonly byte[] Pixels =
    [
        255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 0,
        10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120,
    ];

    private static Ktx2FileBuilder Rgba8(uint width, uint height, byte[] pixels, bool srgb = false)
    {
        var builder = new Ktx2FileBuilder
        {
            VkFormat = srgb ? R8G8B8A8Srgb : R8G8B8A8Unorm,
            PixelWidth = width,
            PixelHeight = height,
            Dfd = Ktx2FileBuilder.Rgba8Dfd(srgb, 0, 1, 2, 15),
        };
        builder.Levels.Add(pixels);
        return builder;
    }

    private static Ktx2FileBuilder Blocks(uint vkFormat, uint width, uint height, int model, (int Width, int Height) block, int bytes, (int ChannelType, int BitOffset, int BitLength, uint Lower, uint Upper)[] samples, byte[] data, int transfer = 1)
    {
        var builder = new Ktx2FileBuilder
        {
            VkFormat = vkFormat,
            PixelWidth = width,
            PixelHeight = height,
            BlockSize = bytes,
            Dfd = Ktx2FileBuilder.BasicDfd(model, 1, transfer, 0, block, [bytes], samples),
        };
        builder.Levels.Add(data);
        return builder;
    }

    private static byte[] BigEndian(params ulong[] words)
    {
        var bytes = new byte[words.Length * 8];
        for (var i = 0; i < words.Length; i++)
        {
            BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(i * 8), words[i]);
        }

        return bytes;
    }

    [Fact]
    public void Rgba8LoadsWithItsLabelAndHeader()
    {
        var data = Rgba8(3, 2, Pixels).AddString("KTXwriter", "test writer").Build();
        using var image = Assert.IsType<Image<Rgba32>>(Image.Load(data));
        Assert.Equal(new Rgba32(255, 0, 0, 255), image.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(0, 255, 0, 128), image.Frames[0][1, 0]);
        Assert.Equal(new Rgba32(90, 100, 110, 120), image.Frames[0][2, 1]);
        Assert.Equal(ImageFormat.Ktx2, image.Metadata.SourceFormat);
        Assert.Equal(ColorTransferFunction.Linear, image.Metadata.TransferFunction);
        var header = Assert.IsType<Ktx2Header>(image.Metadata.Ktx2Header);
        Assert.Equal(37, header.VkFormat);
        Assert.Equal("R8G8B8A8_UNORM", header.FormatName);
        Assert.Equal(0, header.SupercompressionScheme);
        Assert.Equal(1, header.LevelCount);
        Assert.Equal("test writer", header.Writer);
        Assert.Equal(1, header.TransferFunction);

        var info = Image.Identify(data);
        Assert.Equal(ImageFormat.Ktx2, info.Format);
        Assert.Equal(new Size(3, 2), info.Size);
        Assert.Equal(PixelFormat.Rgba32, info.PixelFormat);
        Assert.Equal(1, info.CollectionEntryCount);
        Assert.Equal(ImageFormat.Ktx2, Image.DetectFormat(data));
    }

    [Fact]
    public void SrgbFormatsAreLabeledSrgb()
    {
        using var image = Image.Load(Rgba8(3, 2, Pixels, srgb: true).Build());
        Assert.Equal(ColorTransferFunction.Srgb, image.Metadata.TransferFunction);
        Assert.Null(image.Metadata.Cicp);
    }

    [Fact]
    public void OtherPrimariesAreACicpDescription()
    {
        var builder = Rgba8(3, 2, Pixels, srgb: true);
        builder.Dfd[13] = 10; // Display P3
        using var image = Image.Load(builder.Build());
        Assert.Equal(new CicpColorDescription(12, 13, 0, fullRange: true), image.Metadata.Cicp);
    }

    [Fact]
    public void AnSrgbFormatWithAnotherTransferFunctionIsInvalid()
    {
        var builder = Rgba8(3, 2, Pixels, srgb: true);
        builder.Dfd[14] = 1;
        Assert.Throws<InvalidImageContentException>(() => Image.Load(builder.Build()));
    }

    [Fact]
    public void MissingChannelsAreZeroAndAlphaOne()
    {
        // R8_UNORM: a red texture, as a GPU samples it
        var builder = new Ktx2FileBuilder { VkFormat = 9, PixelWidth = 2, PixelHeight = 1, Dfd = Ktx2FileBuilder.Rgba8Dfd(false, 0) };
        builder.Levels.Add([7, 200]);
        using var image = Assert.IsType<Image<Rgb24>>(Image.Load(builder.Build()));
        Assert.Equal(new Rgb24(7, 0, 0), image.Frames[0][0, 0]);
        Assert.Equal(new Rgb24(200, 0, 0), image.Frames[0][1, 0]);
    }

    [Fact]
    public void LuminanceSwizzlesAreGray()
    {
        // Section 5.4.1: Luminance8 is R8 with rrr1, Luminance8Alpha8 is R8G8 with rrrg
        var gray = new Ktx2FileBuilder { VkFormat = 9, PixelWidth = 2, PixelHeight = 1, Dfd = Ktx2FileBuilder.Rgba8Dfd(false, 0) }.AddString("KTXswizzle", "rrr1");
        gray.Levels.Add([7, 200]);
        using (var image = Assert.IsType<Image<Gray8>>(Image.Load(gray.Build())))
        {
            Assert.Equal(new Gray8(200), image.Frames[0][1, 0]);
        }

        var grayAlpha = new Ktx2FileBuilder { VkFormat = 16, PixelWidth = 2, PixelHeight = 1, Dfd = Ktx2FileBuilder.Rgba8Dfd(false, 0, 1) }.AddString("KTXswizzle", "rrrg");
        grayAlpha.Levels.Add([7, 8, 200, 100]);
        using (var image = Assert.IsType<Image<La16>>(Image.Load(grayAlpha.Build())))
        {
            Assert.Equal(new La16(7, 8), image.Frames[0][0, 0]);
            Assert.Equal(new La16(200, 100), image.Frames[0][1, 0]);
        }

        // Alpha8: 000r
        var alpha = new Ktx2FileBuilder { VkFormat = 9, PixelWidth = 1, PixelHeight = 1, Dfd = Ktx2FileBuilder.Rgba8Dfd(false, 0) }.AddString("KTXswizzle", "000r");
        alpha.Levels.Add([99]);
        using var alphaImage = Image.Load<Rgba32>(alpha.Build());
        Assert.Equal(new Rgba32(0, 0, 0, 99), alphaImage.Frames[0][0, 0]);
    }

    [Theory]
    [InlineData("rgb")]
    [InlineData("rgbx")]
    [InlineData("rgba0")]
    public void MalformedSwizzlesAreInvalid(string swizzle)
    {
        var builder = Rgba8(3, 2, Pixels).AddString("KTXswizzle", swizzle);
        Assert.Throws<InvalidImageContentException>(() => Image.Load(builder.Build()));
    }

    [Fact]
    public void Bgra8IsReordered()
    {
        var builder = new Ktx2FileBuilder { VkFormat = 44, PixelWidth = 1, PixelHeight = 1, Dfd = Ktx2FileBuilder.Rgba8Dfd(false, 2, 1, 0, 15) };
        builder.Levels.Add([1, 2, 3, 4]);
        using var image = Image.Load<Rgba32>(builder.Build());
        Assert.Equal(new Rgba32(3, 2, 1, 4), image.Frames[0][0, 0]);
    }

    [Fact]
    public void PackedFormatsAreTheNearestValueOfTheirFraction()
    {
        // R5G6B5_UNORM_PACK16: B in bits 0-4, G in 5-10, R in 11-15. 31/31, 32/63, 1/31
        var rgb565 = Blocks(4, 1, 1, 1, (1, 1), 2, [(2, 0, 5, 0, 31), (1, 5, 6, 0, 63), (0, 11, 5, 0, 31)], BitConverter.GetBytes((ushort)((31 << 11) | (32 << 5) | 1)));
        rgb565.TypeSize = 2;
        using (var image = Image.Load<Rgba32>(rgb565.Build()))
        {
            Assert.Equal(new Rgba32(255, 130, 8, 255), image.Frames[0][0, 0]);
        }

        // A2B10G10R10_UNORM_PACK32: R 1023, G 512, B 1, A 1 (of 3)
        var rgb10 = Blocks(64, 1, 1, 1, (1, 1), 4, [(0, 0, 10, 0, 1023), (1, 10, 10, 0, 1023), (2, 20, 10, 0, 1023), (15, 30, 2, 0, 3)], BitConverter.GetBytes(1023u | (512u << 10) | (1u << 20) | (1u << 30)));
        rgb10.TypeSize = 4;
        using var deep = Assert.IsType<Image<Rgba64>>(Image.Load(rgb10.Build()));
        Assert.Equal(new Rgba64(65535, 32800, 64, 21845), deep.Frames[0][0, 0]);
    }

    [Fact]
    public void FloatingPointFormatsAreExact()
    {
        // R16G16B16A16_SFLOAT: halves, copied
        var halves = new byte[8];
        BinaryPrimitives.WriteHalfLittleEndian(halves, (Half)1.5f);
        BinaryPrimitives.WriteHalfLittleEndian(halves.AsSpan(2), (Half)(-2f));
        BinaryPrimitives.WriteHalfLittleEndian(halves.AsSpan(4), (Half)65504f);
        BinaryPrimitives.WriteHalfLittleEndian(halves.AsSpan(6), (Half)0.25f);
        var half = Blocks(97, 1, 1, 1, (1, 1), 8, [(0xC0, 0, 16, 0xBF800000, 0x3F800000), (0xC1, 16, 16, 0xBF800000, 0x3F800000), (0xC2, 32, 16, 0xBF800000, 0x3F800000), (0xCF, 48, 16, 0xBF800000, 0x3F800000)], halves);
        half.TypeSize = 2;
        using (var image = Assert.IsType<Image<RgbaHalf>>(Image.Load(half.Build())))
        {
            Assert.Equal(new RgbaHalf((Half)1.5f, (Half)(-2f), (Half)65504f, (Half)0.25f), image.Frames[0][0, 0]);
            Assert.Equal(ColorTransferFunction.Linear, image.Metadata.TransferFunction);
        }

        // E5B9G9R9_UFLOAT_PACK32: m × 2^(e - 24) with the shared exponent 16: R 256 is 1, G 1 is 2^-8, B 511 is 511/256
        var shared = Blocks(123, 1, 1, 1, (1, 1), 4, [(0, 0, 9, 0, 0x2100), (0x20, 27, 5, 15, 31), (1, 9, 9, 0, 0x2100), (0x21, 27, 5, 15, 31), (2, 18, 9, 0, 0x2100), (0x22, 27, 5, 15, 31)], BitConverter.GetBytes(256u | (1u << 9) | (511u << 18) | (16u << 27)));
        shared.TypeSize = 4;
        using (var image = Image.Load<RgbaFloat>(shared.Build()))
        {
            Assert.Equal(new RgbaFloat(1f, MathF.ScaleB(1, -8), 511f / 256, 1f), image.Frames[0][0, 0]);
        }

        // B10G11R11_UFLOAT_PACK32: 1.0 is the exponent 15 with no mantissa in each field
        var small = Blocks(122, 1, 1, 1, (1, 1), 4, [(0x80, 0, 11, 0, 0x3F800000), (0x81, 11, 11, 0, 0x3F800000), (0x82, 22, 10, 0, 0x3F800000)], BitConverter.GetBytes((15u << 6) | (16u << 6 << 11) | (((14u << 5) | 16u) << 22)));
        small.TypeSize = 4;
        using var floats = Image.Load<RgbaFloat>(small.Build());
        Assert.Equal(new RgbaFloat(1f, 2f, 0.75f, 1f), floats.Frames[0][0, 0]);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // ETC2 and EAC (Khronos Data Format Specification, section 22)
    // -----------------------------------------------------------------------------------------------------------------

    private static readonly (int, int, int, uint, uint)[] Etc2ColorSample = [(2, 0, 64, 0, uint.MaxValue)];

    private static Rgba32[] LoadEtc(uint vkFormat, (int, int, int, uint, uint)[] samples, byte[] block, int transfer = 1)
    {
        using var image = Image.Load<Rgba32>(Blocks(vkFormat, 4, 4, 161, (4, 4), block.Length, samples, block, transfer).Build());
        var texels = new Rgba32[16];
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                texels[(y * 4) + x] = image.Frames[0][x, y];
            }
        }

        return texels;
    }

    [Fact]
    public void Etc2IndividualMode()
    {
        // Subblocks side by side: (14, 3, 8) and (4, 11, 9) of 4 bits, tables 0 and 4, every pixel index +b
        var high = (14u << 28) | (4u << 24) | (3u << 20) | (11u << 16) | (8u << 12) | (9u << 8) | (0u << 5) | (4u << 2);
        var texels = LoadEtc(147, Etc2ColorSample, BigEndian(((ulong)high << 32) | 0xFFFF));
        Assert.Equal(new Rgba32(238 + 8, 51 + 8, 136 + 8), texels[0]);
        Assert.Equal(new Rgba32(238 + 8, 51 + 8, 136 + 8), texels[13]);
        Assert.Equal(new Rgba32(68 + 60, 187 + 60, 153 + 60), texels[2]);
    }

    [Fact]
    public void Etc2DifferentialMode()
    {
        // (28, 4, 3) of 5 bits and the deltas (-4, +2, 0): 231, 33, 24 and 198, 49, 24; tables 2 and 3, flipped, index +a
        var high = (28u << 27) | (4u << 24) | (4u << 19) | (2u << 16) | (3u << 11) | (2u << 5) | (3u << 2) | 2u | 1u;
        var texels = LoadEtc(147, Etc2ColorSample, BigEndian((ulong)high << 32));
        Assert.Equal(new Rgba32(231 + 9, 33 + 9, 24 + 9), texels[7]);
        Assert.Equal(new Rgba32(198 + 13, 49 + 13, 24 + 13), texels[8]);
    }

    // The pixel indices of a block whose row y has the index y (0 to 3)
    private const uint RowIndices = (0xCCCCu << 16) | 0xAAAAu;

    [Fact]
    public void Etc2TMode()
    {
        // The example of figure 58: (13, 1, 8) and (4, 12, 13), distance index 5 (32). R + Rd overflows: R = 31, Rd = 1
        var high = (7u << 29) | (3u << 27) | (1u << 24) | (1u << 20) | (8u << 16) | (4u << 12) | (12u << 8) | (13u << 4) | (2u << 2) | 2u | 1u;
        var texels = LoadEtc(147, Etc2ColorSample, BigEndian(((ulong)high << 32) | RowIndices));
        Assert.Equal(new Rgba32(221, 17, 136), texels[0]);
        Assert.Equal(new Rgba32(100, 236, 253), texels[5]);
        Assert.Equal(new Rgba32(68, 204, 221), texels[10]);
        Assert.Equal(new Rgba32(36, 172, 189), texels[15]);
    }

    [Fact]
    public void Etc2HMode()
    {
        // (13, 1, 10) and (4, 12, 13): G + Gd overflows (G = 31, Gd = 1); base color 1 is larger, so the index is 5 (32)
        var high = (13u << 27) | (7u << 21) | (1u << 20) | (1u << 19) | (2u << 15) | (4u << 11) | (12u << 7) | (13u << 3) | (1u << 2) | 2u;
        var texels = LoadEtc(147, Etc2ColorSample, BigEndian(((ulong)high << 32) | RowIndices));
        Assert.Equal(new Rgba32(253, 49, 202), texels[0]);
        Assert.Equal(new Rgba32(189, 0, 138), texels[5]);
        Assert.Equal(new Rgba32(100, 236, 253), texels[10]);
        Assert.Equal(new Rgba32(36, 172, 189), texels[15]);
    }

    [Fact]
    public void Etc2PlanarMode()
    {
        // The example of figure 60: (12, 64, 62), (50, 5, 37), (40, 112, 45) of 6, 7 and 6 bits
        ulong block = (12ul << 57) | (1ul << 56) | (1ul << 48) | (7ul << 45) | (3ul << 43) | (6ul << 39) | (25ul << 34) | (1ul << 33)
            | (5ul << 25) | (37ul << 19) | (40ul << 13) | (112ul << 6) | 45ul;
        var texels = LoadEtc(147, Etc2ColorSample, BigEndian(block));
        Assert.Equal(new Rgba32(48, 129, 251), texels[0]);
        Assert.Equal(new Rgba32(144, 147, 191), texels[9]);
        Assert.Equal(new Rgba32(250, 112, 124), texels[15]);
    }

    [Fact]
    public void Etc2PunchthroughAlpha()
    {
        // The differential block without the opaque bit: no small modifiers, and the index 2 is transparent black
        var high = (28u << 27) | (4u << 24) | (4u << 19) | (2u << 16) | (3u << 11) | (2u << 5) | (3u << 2) | 1u;
        var texels = LoadEtc(149, [(2, 0, 64, 0, uint.MaxValue), (15, 0, 64, 0, uint.MaxValue)], BigEndian(((ulong)high << 32) | RowIndices));
        Assert.Equal(new Rgba32(231, 33, 24, 255), texels[0]);
        Assert.Equal(new Rgba32(255, 62, 53, 255), texels[4]);
        Assert.Equal(new Rgba32(0, 0, 0, 0), texels[8]);
        Assert.Equal(new Rgba32(156, 7, 0, 255), texels[12]);

        // With the opaque bit, the same block is the differential block of RGB ETC2
        var opaque = LoadEtc(149, [(2, 0, 64, 0, uint.MaxValue), (15, 0, 64, 0, uint.MaxValue)], BigEndian(((ulong)(high | 2u) << 32) | RowIndices));
        Assert.Equal(new Rgba32(231 + 9, 33 + 9, 24 + 9, 255), opaque[0]);
        Assert.Equal(new Rgba32(198 - 13, 49 - 13, 24 - 13, 255), opaque[8]);
    }

    private static ulong EacBlock(int baseCodeword, int multiplier, int table, int index)
    {
        var block = ((ulong)(byte)baseCodeword << 56) | ((ulong)multiplier << 52) | ((ulong)table << 48);
        for (var i = 0; i < 16; i++)
        {
            block |= (ulong)index << (45 - (3 * i));
        }

        return block;
    }

    [Fact]
    public void Etc2AlphaBlock()
    {
        // Section 22.3: base 103, table 13 [-1, -2, -3, -10, ...], index 3, multiplier 2: 103 - 20 = 83
        var color = (28u << 27) | (4u << 24) | (4u << 19) | (2u << 16) | (3u << 11) | (2u << 5) | (3u << 2) | 2u | 1u;
        var texels = LoadEtc(151, [(15, 0, 64, 0, uint.MaxValue), (2, 64, 64, 0, uint.MaxValue)], BigEndian(EacBlock(103, 2, 13, 3), (ulong)color << 32));
        Assert.Equal(83, texels[6].A);
        Assert.Equal(240, texels[0].R);
    }

    [Fact]
    public void EacR11IsElevenBitsReplicatedToSixteen()
    {
        // Section 22.5: 103 × 8 + 4 - 160 = 668, and 828 - 10 = 818 when the multiplier is 0
        foreach (var (multiplier, value) in new[] { (2, 668), (0, 818) })
        {
            using var image = Image.Load<Rgba64>(Blocks(153, 4, 4, 161, (4, 4), 8, [(0, 0, 64, 0, uint.MaxValue)], BigEndian(EacBlock(103, multiplier, 13, 3))).Build());
            Assert.Equal(new Rgba64((ushort)((value << 5) | (value >> 6)), 0, 0, 65535), image.Frames[0][1, 2]);
        }
    }

    [Fact]
    public void SignedEacIsAFloatOfTheElevenBitValue()
    {
        // Section 22.7: 60 × 8 - 160 = 320, and 480 - 10 = 470 when the multiplier is 0; -128 reads as -127
        foreach (var (baseCodeword, multiplier, value) in new[] { (60, 2, 320), (60, 0, 470), (-128, 1, -1023) })
        {
            using var image = Assert.IsType<Image<RgbaFloat>>(Image.Load(Blocks(154, 4, 4, 161, (4, 4), 8, [(0x40, 0, 64, 0x80000000, 0x7FFFFFFF)], BigEndian(EacBlock(baseCodeword, multiplier, 13, 3))).Build()));
            Assert.Equal(new RgbaFloat(value / 1023f, 0, 0, 1), image.Frames[0][3, 3]);
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // ASTC (section 23) and UASTC (section 25)
    // -----------------------------------------------------------------------------------------------------------------

    private static byte[] VoidExtent(bool hdr, ushort r, ushort g, ushort b, ushort a)
    {
        // Table 185: 111111100 in the low bits, the dynamic range bit, the reserved bits 10 and 11, extents of all ones
        var block = new byte[16];
        ulong low = 0x1FCul | (hdr ? 1ul << 9 : 0) | (3ul << 10) | (0xFFFFFFFFFFFFFul << 12);
        BinaryPrimitives.WriteUInt64LittleEndian(block, low);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(8), r);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(10), g);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(12), b);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(14), a);
        return block;
    }

    [Fact]
    public void AstcVoidExtentInEachOperationMode()
    {
        var block = VoidExtent(hdr: false, 0x8000, 0xFFFF, 0x0100, 0x7FFF);

        // sRGB: the top 8 bits
        using (var image = Image.Load<Rgba32>(Blocks(166, 6, 6, 162, (6, 6), 16, [(0, 0, 128, 0, uint.MaxValue)], block, transfer: 2).Build()))
        {
            Assert.Equal(new Rgba32(0x80, 0xFF, 0x01, 0x7F), image.Frames[0][5, 5]);
        }

        // Linear LDR, decode_float16: C / 65536 toward zero, 1.0 for 65535
        using (var image = Assert.IsType<Image<RgbaHalf>>(Image.Load(Blocks(165, 6, 6, 162, (6, 6), 16, [(0, 0, 128, 0, uint.MaxValue)], block).Build())))
        {
            Assert.Equal(new RgbaHalf((Half)0.5f, Half.One, BitConverter.UInt16BitsToHalf(0x1C00), BitConverter.UInt16BitsToHalf(0x37FF)), image.Frames[0][0, 0]);
        }

        // KTXastcDecodeMode unorm8: the top 8 bits
        var unorm8 = Blocks(165, 6, 6, 162, (6, 6), 16, [(0, 0, 128, 0, uint.MaxValue)], block).AddString("KTXastcDecodeMode", "unorm8");
        using (var image = Assert.IsType<Image<Rgba32>>(Image.Load(unorm8.Build())))
        {
            Assert.Equal(new Rgba32(0x80, 0xFF, 0x01, 0x7F), image.Frames[0][1, 1]);
        }

        // HDR: the halves of the block
        var hdr = VoidExtent(hdr: true, BitConverter.HalfToUInt16Bits((Half)4f), BitConverter.HalfToUInt16Bits((Half)0.5f), 0, BitConverter.HalfToUInt16Bits(Half.One));
        using (var image = Image.Load<RgbaHalf>(Blocks(1000066004, 6, 6, 162, (6, 6), 16, [(0xC0, 0, 128, 0xBF800000, 0x3F800000)], hdr).Build()))
        {
            Assert.Equal(new RgbaHalf((Half)4f, (Half)0.5f, Half.Zero, Half.One), image.Frames[0][2, 3]);
        }

        // An HDR void extent in an LDR format is the error color of the specification: rejected
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Blocks(165, 6, 6, 162, (6, 6), 16, [(0, 0, 128, 0, uint.MaxValue)], hdr).Build()));
    }

    [Fact]
    public void IllegalAstcBlocksAreInvalid()
    {
        // A reserved block mode (bits 3..0 of 0000), and a void extent whose reserved bits are 0
        var reserved = new byte[16];
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Blocks(157, 4, 4, 162, (4, 4), 16, [(0, 0, 128, 0, uint.MaxValue)], reserved).Build()));
        var block = VoidExtent(hdr: false, 1, 2, 3, 4);
        block[1] &= 0xF3;
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Blocks(157, 4, 4, 162, (4, 4), 16, [(0, 0, 128, 0, uint.MaxValue)], block).Build()));
    }

    private static Ktx2FileBuilder Uastc(byte[] blocks, uint width = 4, uint height = 4, int channel = 3)
    {
        var builder = new Ktx2FileBuilder
        {
            VkFormat = 0,
            PixelWidth = width,
            PixelHeight = height,
            BlockSize = 16,
            Dfd = Ktx2FileBuilder.BasicDfd(166, 1, 2, 0, (4, 4), [16], (channel, 0, 128, 0, uint.MaxValue)),
        };
        builder.Levels.Add(blocks);
        return builder;
    }

    private static byte[] UastcSolid(byte r, byte g, byte b, byte a)
    {
        // Table 200: the 5-bit mode code 10111 of the mode 8, then R, G, B and A
        var block = new byte[16];
        var bits = 0x17ul | ((ulong)r << 5) | ((ulong)g << 13) | ((ulong)b << 21) | ((ulong)a << 29);
        BinaryPrimitives.WriteUInt64LittleEndian(block, bits);
        return block;
    }

    [Fact]
    public void UastcSolidColorBlocks()
    {
        using var image = Image.Load<Rgba32>(Uastc([.. UastcSolid(10, 20, 30, 40), .. UastcSolid(250, 0, 128, 255)], width: 6).Build());
        Assert.Equal(new Rgba32(10, 20, 30, 40), image.Frames[0][3, 3]);
        Assert.Equal(new Rgba32(250, 0, 128, 255), image.Frames[0][5, 0]);
    }

    [Fact]
    public void ReservedUastcModesAreInvalid()
    {
        // The mode 19 (1000101), and the mode 7 of a pattern larger than its 19 patterns
        var reserved = new byte[16];
        reserved[0] = 0x45;
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Uastc(reserved).Build()));
        var pattern = new byte[16];
        BinaryPrimitives.WriteUInt64LittleEndian(pattern, 0x07ul | (31ul << 20));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Uastc(pattern).Build()));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Subresources, supercompression and animations
    // -----------------------------------------------------------------------------------------------------------------

    private static byte[] Solid(int pixels, byte r, byte g, byte b, byte a = 255)
    {
        var data = new byte[pixels * 4];
        for (var i = 0; i < pixels; i++)
        {
            (data[i * 4], data[(i * 4) + 1], data[(i * 4) + 2], data[(i * 4) + 3]) = (r, g, b, a);
        }

        return data;
    }

    private static void AssertSurface(ImageCollectionEntry entry, int mipLevel, int arrayIndex, int? face, int depthSlice)
    {
        var surface = Assert.IsType<TextureSurface>(entry.TextureSurface);
        Assert.Equal((mipLevel, arrayIndex, face, depthSlice), (surface.MipLevel, surface.ArrayIndex, (int?)surface.Face, surface.DepthSlice));
    }

    [Fact]
    public void SubresourcesAreOrderedByLayerFaceLevelAndSlice()
    {
        // A cube map array of 2 layers, 2 levels of 4x4 and 2x2: each image is a solid color (layer, face, level)
        var builder = new Ktx2FileBuilder { VkFormat = R8G8B8A8Unorm, PixelWidth = 4, PixelHeight = 4, LayerCount = 2, FaceCount = 6, Dfd = Ktx2FileBuilder.Rgba8Dfd(false, 0, 1, 2, 15) };
        foreach (var size in new[] { 16, 4 })
        {
            var level = new List<byte>();
            for (var layer = 0; layer < 2; layer++)
            {
                for (var face = 0; face < 6; face++)
                {
                    level.AddRange(Solid(size, (byte)layer, (byte)face, (byte)size));
                }
            }

            builder.Levels.Add([.. level]);
        }

        var data = builder.Build();
        using var collection = ImageCollection.Load(data);
        Assert.Equal(ImageCollectionKind.TextureSurfaces, collection.Kind);
        Assert.Equal(ImageFormat.Ktx2, collection.Format);
        Assert.HasCount(24, collection.Entries);
        AssertSurface(collection[0], 0, 0, 0, 0);
        AssertSurface(collection[1], 1, 0, 0, 0);
        AssertSurface(collection[2], 0, 0, 1, 0);
        AssertSurface(collection[23], 1, 1, 5, 0);
        Assert.Equal(new Size(2, 2), collection[23].Size);
        using (var entry = collection[23].Decode<Rgba32>())
        {
            Assert.Equal(new Rgba32(1, 5, 4), entry.Frames[0][1, 1]);
        }

        using (var entry = collection[14].Decode<Rgba32>())
        {
            Assert.Equal(new Rgba32(1, 1, 16), entry.Frames[0][3, 3]);
        }

        using var image = Image.Load<Rgba32>(data);
        Assert.Equal(new Size(4, 4), image.Size);
        Assert.Equal(new Rgba32(0, 0, 16), image.Frames[0][0, 0]);
        Assert.Equal(24, Image.Identify(data).CollectionEntryCount);
    }

    [Fact]
    public void ThreeDimensionalLevelsHaveOneEntryPerSlice()
    {
        // 2x2x4 and 1x1x2: the slices of a level halve with it
        var builder = new Ktx2FileBuilder { VkFormat = R8G8B8A8Unorm, PixelWidth = 2, PixelHeight = 2, PixelDepth = 4, Dfd = Ktx2FileBuilder.Rgba8Dfd(false, 0, 1, 2, 15) }.AddString("KTXorientation", "rdi");
        builder.Levels.Add([.. Solid(4, 0, 0, 0), .. Solid(4, 1, 0, 0), .. Solid(4, 2, 0, 0), .. Solid(4, 3, 0, 0)]);
        builder.Levels.Add([.. Solid(1, 0, 1, 0), .. Solid(1, 1, 1, 0)]);
        using var collection = ImageCollection.Load(builder.Build());
        Assert.HasCount(6, collection.Entries);
        AssertSurface(collection[3], 0, 0, null, 3);
        AssertSurface(collection[5], 1, 0, null, 1);
        using var slice = collection[3].Decode<Rgba32>();
        Assert.Equal(new Rgba32(3, 0, 0), slice.Frames[0][1, 1]);
        using var small = collection[5].Decode<Rgba32>();
        Assert.Equal(new Rgba32(1, 1, 0), small.Frames[0][0, 0]);
    }

    [Fact]
    public void ZlibLevelsAreInflated()
    {
        var builder = new Ktx2FileBuilder { VkFormat = R8G8B8A8Unorm, PixelWidth = 3, PixelHeight = 2, SupercompressionScheme = 3, Dfd = Ktx2FileBuilder.Rgba8Dfd(false, 0, 1, 2, 15) };
        builder.Levels.Add(Ktx2FileBuilder.Zlib(Pixels));
        builder.UncompressedLengths.Add((ulong)Pixels.Length);
        using var image = Image.Load<Rgba32>(builder.Build());
        Assert.Equal(new Rgba32(50, 60, 70, 80), image.Frames[0][1, 1]);

        // The stream must produce exactly the uncompressed length
        builder.UncompressedLengths[0] = (ulong)Pixels.Length;
        builder.Levels[0] = Ktx2FileBuilder.Zlib(Pixels.AsSpan(0, 20));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(builder.Build()));
    }

    [Theory]
    [InlineData("rd", ExifOrientation.TopLeft)]
    [InlineData("ru", ExifOrientation.BottomLeft)]
    [InlineData("ld", ExifOrientation.TopRight)]
    [InlineData("lu", ExifOrientation.BottomRight)]
    public void OrientationIsReported(string orientation, ExifOrientation expected)
    {
        using var image = Image.Load(Rgba8(3, 2, Pixels).AddString("KTXorientation", orientation).Build());
        Assert.Equal(expected, image.Metadata.Orientation);
        Assert.Equal(orientation, image.Metadata.Ktx2Header!.Orientation);

        // Only AutoOrient applies it: the first stored row is still the first row
        Assert.Equal(new Rgba32(255, 0, 0, 255), ((Image<Rgba32>)image).Frames[0][0, 0]);
    }

    [Theory]
    [InlineData("r")]
    [InlineData("rdo")]
    [InlineData("xd")]
    public void MalformedOrientationsAreInvalid(string orientation)
        => Assert.Throws<InvalidImageContentException>(() => Image.Load(Rgba8(3, 2, Pixels).AddString("KTXorientation", orientation).Build()));

    [Fact]
    public void AnimationsAreTheLayersOfTheBaseLevel()
    {
        // KTXanimData: 3 time units of 1/60 s per frame, played twice
        var builder = new Ktx2FileBuilder { VkFormat = R8G8B8A8Unorm, PixelWidth = 2, PixelHeight = 1, LayerCount = 3, Dfd = Ktx2FileBuilder.Rgba8Dfd(false, 0, 1, 2, 15) };
        builder.KeyValues.Add(new("KTXanimData", [3, 0, 0, 0, 60, 0, 0, 0, 2, 0, 0, 0]));
        builder.Levels.Add([.. Solid(2, 1, 0, 0), .. Solid(2, 2, 0, 0), .. Solid(2, 3, 0, 0)]);
        var data = builder.Build();
        using var image = Image.Load<Rgba32>(data);
        Assert.True(image.IsAnimated);
        Assert.HasCount(3, image.Frames);
        Assert.Equal(new FrameDuration(3, 60), image.Frames[1].Metadata.Duration);
        Assert.Equal(2, image.Animation!.TotalPlays);
        Assert.Equal(new Rgba32(3, 0, 0), image.Frames[2][1, 0]);
        var info = Image.Identify(data);
        Assert.Equal(3, info.FrameCount);
        Assert.True(info.IsAnimated);

        // A collection still has one entry per layer
        using var collection = ImageCollection.Load(data);
        Assert.HasCount(3, collection.Entries);
    }

    [Fact]
    public void ConvertedFloatingPointAnimationsAreCheckedAgainstTheLimitsBeforeTheyAreDecoded()
    {
        // Three frames of halves, converted with a tone mapping: the frames are staged, so the limits are checked first
        var halves = new byte[3 * 8];
        for (var i = 0; i < halves.Length; i += 2)
        {
            BinaryPrimitives.WriteHalfLittleEndian(halves.AsSpan(i), (Half)(i / 4f));
        }

        var builder = Blocks(97, 1, 1, 1, (1, 1), 8, [(0xC0, 0, 16, 0xBF800000, 0x3F800000), (0xC1, 16, 16, 0xBF800000, 0x3F800000), (0xC2, 32, 16, 0xBF800000, 0x3F800000), (0xCF, 48, 16, 0xBF800000, 0x3F800000)], halves);
        builder.TypeSize = 2;
        builder.LayerCount = 3;
        builder.KeyValues.Add(new("KTXanimData", [1, 0, 0, 0, 30, 0, 0, 0, 0, 0, 0, 0]));
        var data = builder.Build();
        var conversion = new PixelConversionOptions { ToneMapping = new HdrToneMappingOptions(), OutOfRangeSamples = OutOfRangeSampleHandling.Clip };
        using (var image = Image.Load<Rgba64>(data, new ImageDecodeOptions { Conversion = conversion }))
        {
            Assert.HasCount(3, image.Frames);
        }

        foreach (var (limits, kind) in new[] { (new ImageResourceLimits { MaxFrames = 2 }, ImageResourceLimitKind.Frames), (new ImageResourceLimits { MaxTotalPixels = 2 }, ImageResourceLimitKind.TotalPixels) })
        {
            var options = new ImageDecodeOptions { Conversion = conversion, Configuration = new ImageConfiguration { Limits = limits } };
            Assert.Equal(kind, Assert.Throws<ImageResourceLimitException>(() => Image.Load<Rgba64>(data, options)).Kind);
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Errors and limits
    // -----------------------------------------------------------------------------------------------------------------

    public static TheoryData<string> InvalidEdits => ["identifier", "type-size", "level-length", "level-offset", "dfd-model", "dfd-sample", "dfd-total-size", "key-length", "duplicate-key", "face-count", "levels", "sgd", "cube-size", "truncated"];

    [Theory]
    [MemberData(nameof(InvalidEdits))]
    public void MalformedFilesAreInvalid(string edit)
    {
        var builder = Rgba8(3, 2, Pixels);
        switch (edit)
        {
            case "identifier":
                builder.Edit = file => file[11] = 0;
                break;
            case "type-size":
                builder.TypeSize = 4;
                break;
            case "level-length":
                builder.Edit = file => file[88] = 23;
                break;
            case "level-offset":
                builder.Edit = file => file[80] = 1;
                break;
            case "dfd-model":
                builder.Dfd[12] = 2;
                break;
            case "dfd-sample":
                builder.Dfd[28 + 3] = 1;
                break;
            case "dfd-total-size":
                builder.Dfd[0]++;
                break;
            case "key-length":
                builder.AddString("KTXwriter", "x");
                builder.Edit = file => file[80 + 24 + builder.Dfd.Length] = 200;
                break;
            case "duplicate-key":
                builder.AddString("a", "1").AddString("a", "2");
                break;
            case "face-count":
                builder.FaceCount = 2;
                break;
            case "levels":
                builder.LevelCount = 4;
                break;
            case "sgd":
                builder.GlobalData = [1, 2, 3, 4];
                break;
            case "cube-size":
                builder.FaceCount = 6;
                break;
            default:
                var data = builder.Build();
                Assert.Throws<InvalidImageContentException>(() => Image.Load(data.AsSpan(0, data.Length - 1)));
                return;
        }

        var file = builder.Build();
        if (edit == "identifier")
        {
            Assert.Throws<UnknownImageFormatException>(() => Image.Load(file));
            return;
        }

        Assert.Throws<InvalidImageContentException>(() => Image.Load(file));
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(file));
    }

    [Theory]
    [InlineData(1000054000u, "Format: PVRTC")]
    [InlineData(1000156000u, "Format: YCbCr")]
    [InlineData(1000288000u, "Format: ASTC 3D")]
    [InlineData(38u, "Format: 38")]
    public void UnsupportedFormatsAreRejected(uint vkFormat, string feature)
    {
        var builder = Rgba8(3, 2, Pixels);
        builder.VkFormat = vkFormat;
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(builder.Build()));
        Assert.Equal(feature, exception.Feature);
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(builder.Build()));
    }

    [Fact]
    public void PremultipliedAlphaIsUnsupported()
    {
        var builder = Rgba8(3, 2, Pixels);
        builder.Dfd[15] = 1;
        Assert.Equal("Premultiplied alpha", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(builder.Build())).Feature);
    }

    [Fact]
    public void VendorSupercompressionIsUnsupported()
    {
        var builder = Rgba8(3, 2, Pixels);
        builder.SupercompressionScheme = 0x10000;
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(builder.Build()));
    }

    [Fact]
    public void LimitsAreEnforced()
    {
        var data = Rgba8(3, 2, Pixels).AddString("KTXwriter", new string('w', 100)).Build();
        var width = Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, new ImageDecodeOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 2 } } }));
        Assert.Equal(ImageResourceLimitKind.Width, width.Kind);
        var metadata = Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, new ImageDecodeOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxMetadataBytes = 100 } } }));
        Assert.Equal(ImageResourceLimitKind.MetadataBytes, metadata.Kind);
        using (Image.Load(data, new ImageDecodeOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 3 } } }))
        {
        }

        var array = Rgba8(3, 2, [.. Pixels, .. Pixels]);
        array.LayerCount = 2;
        var frames = Assert.Throws<ImageResourceLimitException>(() => ImageCollection.Load(array.Build(), new ImageConfiguration { Limits = new ImageResourceLimits { MaxFrames = 1 } }).Dispose());
        Assert.Equal(ImageResourceLimitKind.Frames, frames.Kind);
    }

    [Fact]
    public void Ktx2DefaultFileExtension()
    {
        Assert.Equal(".ktx2", ImageFormatInfo.Get(ImageFormat.Ktx2).DefaultFileExtension);
    }
}
