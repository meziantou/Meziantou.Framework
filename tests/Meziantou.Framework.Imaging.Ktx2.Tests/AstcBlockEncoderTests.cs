using System.Buffers.Binary;
using System.Security.Cryptography;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The ASTC LDR block encoder (Khronos Data Format Specification 1.4, section 23): every block decodes with
/// <see cref="AstcBlockDecoder"/>, within the error bounds measured for each content, footprint and quality level.
/// </summary>
public sealed class AstcBlockEncoderTests
{
    private const int ImageSize = 48;

    public static TheoryData<int, int> Footprints()
    {
        var data = new TheoryData<int, int>();
        foreach (var (width, height) in AstcFootprintTables.Footprints)
        {
            data.Add(width, height);
        }

        return data;
    }

    // Content, footprint, quality level, sRGB: the minimum PSNR (dB) and the maximum absolute error of a sample (8-bit units)
    public static TheoryData<string, int, int, bool, bool, double, double> RoundTripCases() => new()
    {
        { "photo", 4, 4, false, true, 59.5, 3 },
        { "photo", 4, 4, true, true, 60, 3 },
        { "photo", 4, 4, false, false, 59.5, 3 },
        { "photo", 6, 6, false, true, 56.5, 4 },
        { "photo", 6, 6, true, true, 57, 4 },
        { "photo", 8, 8, false, true, 54.5, 4 },
        { "photo", 8, 8, true, true, 55, 4 },
        { "photo", 12, 12, false, true, 52, 4 },
        { "photo", 12, 12, true, true, 52, 5 },
        { "photo", 10, 6, false, false, 55.5, 4 },
        { "gradient-alpha", 4, 4, false, true, 44, 8 },
        { "gradient-alpha", 4, 4, true, false, 44, 8 },
        { "gradient-alpha", 5, 4, false, true, 42.5, 9 },
        { "gradient-alpha", 8, 8, false, true, 38, 13 },
        { "gradient-alpha", 8, 8, true, true, 38, 13 },
        { "gradient-alpha", 12, 12, false, false, 34.5, 21 },
        { "edges", 4, 4, false, true, 53, 34 },
        { "edges", 4, 4, true, true, 61.5, 4 },
        { "edges", 6, 6, false, true, 39.5, 161 },
        { "edges", 6, 6, true, true, 51.5, 13 },
        { "edges", 8, 8, true, false, 38.5, 111 },
        { "edges", 12, 12, true, true, 24, 250 },
    };

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void RoundTrip_ErrorIsBounded(string content, int blockWidth, int blockHeight, bool thorough, bool srgb, double minimumPsnr, double maximumError)
    {
        var (psnr, maxError) = RoundTrip(content, blockWidth, blockHeight, srgb, thorough);
        Assert.InRange(psnr, minimumPsnr, double.MaxValue, message: $"PSNR {psnr:F2}");
        Assert.InRange(maxError.Max(), 0, maximumError, message: $"maximum error {maxError.Max():F1}");
    }

    [Theory]
    [MemberData(nameof(Footprints))]
    public void SolidColors_AreExactVoidExtents(int blockWidth, int blockHeight)
    {
        byte[][] colors = [[0, 0, 0, 0], [255, 255, 255, 255], [12, 200, 77, 128], [1, 2, 254, 0]];
        foreach (var color in colors)
        {
            var texels = new byte[blockWidth * blockHeight * 4];
            for (var i = 0; i < texels.Length; i += 4)
            {
                color.CopyTo(texels, i);
            }

            foreach (var srgb in new[] { true, false })
            {
                foreach (var thorough in new[] { false, true })
                {
                    var block = new byte[16];
                    AstcBlockEncoder.Encode(texels, blockWidth, blockHeight, srgb, block, thorough);
                    Assert.Equal("void", Describe(block));

                    // The extent is all ones (no extent), the color the UNORM16 values v × 257
                    Assert.Equal(0xFFFF_FFFF_FFFF_FDFCUL, BinaryPrimitives.ReadUInt64LittleEndian(block));
                    var decoded = Decode(block, blockWidth, blockHeight, srgb ? AstcProfile.Srgb : AstcProfile.LinearUnorm8);
                    Assert.Equal(texels, decoded);
                    var halves = Decode(block, blockWidth, blockHeight, AstcProfile.Linear);
                    for (var c = 0; c < 4; c++)
                    {
                        var expected = color[c] == 255 ? 1.0 : color[c] * 257 / 65536.0;
                        Assert.Equal(expected, (double)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(halves.AsSpan(2 * c))), 1e-3);
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Footprints))]
    public void RandomBlocks_DecodeInEveryProfile(int blockWidth, int blockHeight)
    {
        var count = blockWidth * blockHeight;
        var state = (ulong)((blockWidth * 16) + blockHeight);
        var texels = new byte[count * 4];
        var block = new byte[16];
        for (var kind = 0; kind < 8; kind++)
        {
            for (var i = 0; i < count; i++)
            {
                var r = Next(ref state);
                var g = Next(ref state);
                var b = Next(ref state);
                var a = Next(ref state);
                (texels[i * 4], texels[(i * 4) + 1], texels[(i * 4) + 2], texels[(i * 4) + 3]) = kind switch
                {
                    0 => (r, g, b, (byte)255),
                    1 => (r, g, b, a),
                    2 => (r, r, r, (byte)255),
                    3 => (r, r, r, a),
                    4 => (r < 128 ? (byte)0 : (byte)255, r < 128 ? (byte)0 : (byte)255, r < 128 ? (byte)0 : (byte)255, r < 64 ? (byte)0 : (byte)255),
                    5 => ((byte)(r >> 6), (byte)(r >> 6), (byte)((r >> 6) + 1), (byte)255),
                    6 => ((byte)(i * 255 / count), (byte)(255 - (i * 255 / count)), (byte)(r >> 4), a),
                    _ => (r < 100 ? (byte)250 : (byte)10, r < 200 ? (byte)30 : (byte)220, g, (byte)255),
                };
            }

            foreach (var srgb in new[] { true, false })
            {
                foreach (var thorough in new[] { false, true })
                {
                    if (thorough && kind % 2 != 0 && count > 64)
                        continue;

                    AstcBlockEncoder.Encode(texels, blockWidth, blockHeight, srgb, block, thorough);
                    Decode(block, blockWidth, blockHeight, AstcProfile.Srgb);
                    Decode(block, blockWidth, blockHeight, AstcProfile.Linear);
                    Decode(block, blockWidth, blockHeight, AstcProfile.LinearUnorm8);
                    Assert.Contains(Describe(block)[2..], (string[])["void", "c0", "c4", "c6", "c8", "c10", "c12", "c0d", "c4d", "c6d", "c8d", "c10d", "c12d"]);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Footprints))]
    public void ExtremeInputs_DecodeWithinBounds(int blockWidth, int blockHeight)
    {
        var count = blockWidth * blockHeight;
        var block = new byte[16];
        foreach (var srgb in new[] { true, false })
        {
            foreach (var thorough in new[] { false, true })
            {
                // Black and white texels, transparent and opaque: two colors on a line, exact while the weight grid can be
                // the footprint
                var texels = new byte[count * 4];
                for (var i = 0; i < count; i++)
                {
                    var value = (byte)((i + (i / blockWidth)) % 2 == 0 ? 0 : 255);
                    texels.AsSpan(i * 4, 4).Fill(value);
                }

                AstcBlockEncoder.Encode(texels, blockWidth, blockHeight, srgb, block, thorough);
                var decoded = Decode(block, blockWidth, blockHeight, srgb ? AstcProfile.Srgb : AstcProfile.LinearUnorm8);
                if (count <= 64)
                {
                    Assert.Equal(texels, decoded);
                }
                else
                {
                    Assert.InRange(MeanAbsoluteError(texels, decoded), 0, 128);
                }
            }
        }
    }

    // The modes of crafted 6x6 and 12x12 blocks ("p" the partitions, "c" the endpoint mode, "d" two planes) and the bound
    // of their mean absolute error (8-bit units)
    [Theory]
    [InlineData("gray-ramp", 6, false, "p1c0", 2)]
    [InlineData("gray-alpha", 6, false, "p1c4d", 4)]
    [InlineData("gray-alpha-same", 6, false, "p1c4", 1)]
    [InlineData("two-colors", 6, false, "p1c8", 1)]
    [InlineData("rgba-line", 6, false, "p1c12", 2)]
    [InlineData("rgb-plane", 6, false, "p1c8d", 4)]
    [InlineData("rgba-alpha-indep", 6, false, "p1c12d", 3)]
    [InlineData("three-colors", 6, false, "p2c8", 8)]
    [InlineData("three-colors", 6, true, "p3c8", 5)]
    [InlineData("three-colors-rgba", 6, true, "p2c12", 20)]
    [InlineData("near-gray", 6, true, "p1c6", 2)]
    [InlineData("near-gray", 12, true, "p1c0", 2)]
    [InlineData("scaled-rgba", 12, true, "p1c10", 2)]
    [InlineData("two-colors", 12, true, "p2c6", 4)]
    public void CraftedBlocks_UseTheExpectedModes(string content, int size, bool thorough, string expected, double maximumMeanError)
    {
        var texels = Crafted(content, size);
        foreach (var srgb in new[] { true, false })
        {
            var block = new byte[16];
            AstcBlockEncoder.Encode(texels, size, size, srgb, block, thorough);
            Assert.Equal(expected, Describe(block));
            var decoded = Decode(block, size, size, srgb ? AstcProfile.Srgb : AstcProfile.LinearUnorm8);
            Assert.InRange(MeanAbsoluteError(texels, decoded), 0, maximumMeanError);
        }
    }

    [Theory]
    [InlineData(false, "b863d2b978091968c6f64bf9ede318b7ee07e9779af81bad09e1657fd47b1edf")]
    [InlineData(true, "e77f43315d840d9f5e1f8665477773b7a072bddc532c6fddfcf26b5a0c552870")]
    public void Encoding_IsDeterministic(bool thorough, string expectedHash)
    {
        // A fixed pseudo-random mix of noise, gradients and flat areas, for every footprint and both operation modes
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var block = new byte[16];
        foreach (var (width, height) in AstcFootprintTables.Footprints)
        {
            var count = width * height;
            var state = 0x2545F4914F6CDD1DUL;
            var texels = new byte[count * 4];
            for (var kind = 0; kind < (thorough ? 2 : 4); kind++)
            {
                for (var i = 0; i < count; i++)
                {
                    var noise = Next(ref state);
                    for (var c = 0; c < 4; c++)
                    {
                        texels[(i * 4) + c] = kind switch
                        {
                            0 => (byte)(((i % width) * 20) + (c * 30) + (noise >> 5)),
                            1 => c == 3 ? (byte)((i / width) * 20) : (byte)(noise < 128 ? 40 + (c * 50) : 200 - (c * 30)),
                            2 => Next(ref state),
                            _ => (byte)((i / width) * 255 / height),
                        };
                    }
                }

                foreach (var srgb in new[] { true, false })
                {
                    AstcBlockEncoder.Encode(texels, width, height, srgb, block, thorough);
                    hash.AppendData(block);
                }
            }
        }

        Assert.Equal(expectedHash, Convert.ToHexStringLower(hash.GetHashAndReset()));
    }

    [Fact]
    public void IntegerSequences_RoundTripEveryValueOfEveryRange()
    {
        // The ranges of the endpoints and of the weights, every length that fits in a block after the 17 bits of a header
        var ranges = AstcBlockDecoder.ColorRanges.Concat(AstcEncodingTables.WeightRanges).Distinct().ToArray();
        var decoded = new int[128];
        foreach (var (trits, quints, bits) in ranges)
        {
            var levels = AstcEncodingTables.GetLevelCount(trits, quints, bits);
            for (var count = 1; AstcBlockDecoder.GetSequenceBits(count, trits, quints, bits) <= 111; count++)
            {
                for (var offset = 0; offset < levels; offset++)
                {
                    var values = new int[count];
                    for (var i = 0; i < count; i++)
                    {
                        values[i] = (offset + (i * 7)) % levels;
                    }

                    UInt128 block = 0;
                    AstcEncodingTables.WriteSequence(ref block, 17, values, trits, quints, bits);
                    var end = 17 + AstcBlockDecoder.GetSequenceBits(count, trits, quints, bits);
                    Assert.Equal(UInt128.Zero, block & ((UInt128.One << 17) - 1));
                    Assert.Equal(UInt128.Zero, end == 128 ? UInt128.Zero : block >> end);
                    AstcBlockDecoder.DecodeSequence(block, 17, count, trits, quints, bits, decoded);
                    Assert.Equal(values, decoded[..count]);
                }
            }
        }
    }

    [Fact]
    public void Quantization_IsTheNearestLevel()
    {
        for (var range = 0; range < AstcBlockDecoder.ColorRanges.Length; range++)
        {
            var (trits, quints, bits) = AstcBlockDecoder.ColorRanges[range];
            var levels = AstcEncodingTables.GetLevelCount(trits, quints, bits);
            for (var value = 0; value < 256; value++)
            {
                var quantized = AstcEncodingTables.QuantizeColor(range, value);
                Assert.Equal(AstcBlockDecoder.UnquantizeColor(quantized, trits, quints, bits), AstcEncodingTables.UnquantizeColor(range, quantized));
                var distance = Math.Abs(AstcBlockDecoder.UnquantizeColor(quantized, trits, quints, bits) - value);
                for (var other = 0; other < levels; other++)
                {
                    Assert.InRange(Math.Abs(AstcBlockDecoder.UnquantizeColor(other, trits, quints, bits) - value), distance, 255);
                }
            }
        }

        for (var range = 0; range < AstcEncodingTables.WeightRangeCount; range++)
        {
            var (trits, quints, bits) = AstcEncodingTables.WeightRanges[range];
            var levels = AstcEncodingTables.GetLevelCount(trits, quints, bits);
            for (var rank = 0; rank < levels; rank++)
            {
                Assert.Equal(AstcBlockDecoder.UnquantizeWeight(AstcEncodingTables.GetRankValue(range, rank), trits, quints, bits), AstcEncodingTables.GetRankWeight(range, rank));
                if (rank > 0)
                {
                    Assert.InRange(AstcEncodingTables.GetRankWeight(range, rank), AstcEncodingTables.GetRankWeight(range, rank - 1) + 1, 64);
                }
            }

            for (var weight = 0; weight <= 64; weight++)
            {
                var distance = Math.Abs(AstcEncodingTables.GetRankWeight(range, AstcEncodingTables.GetNearestWeightRank(range, weight)) - weight);
                for (var rank = 0; rank < levels; rank++)
                {
                    Assert.InRange(Math.Abs(AstcEncodingTables.GetRankWeight(range, rank) - weight), distance, 64);
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Footprints))]
    public void Configurations_AreLegalBlockModes(int blockWidth, int blockHeight)
    {
        var tables = AstcFootprintTables.Get(AstcFootprintTables.GetFootprintIndex(blockWidth, blockHeight));
        foreach (var multiple in new[] { false, true })
        {
            foreach (var dual in new[] { false, true })
            {
                for (var values = 2; values <= 18; values += 2)
                {
                    foreach (var config in tables.GetConfigs(multiple, dual, values))
                    {
                        Assert.True(AstcBlockDecoder.TryDecodeBlockMode(config.BlockMode, out var width, out var height, out var dualPlane, out var range));
                        Assert.Equal((config.GridWidth, config.GridHeight, dual, AstcEncodingTables.WeightRanges[config.WeightRange]), (width, height, dualPlane, range));
                        Assert.InRange(width, 2, blockWidth);
                        Assert.InRange(height, 2, blockHeight);
                        Assert.InRange(width * height * (dual ? 2 : 1), 1, 64);
                    }
                }
            }
        }
    }

    [Fact]
    public void InvalidArguments_Throw()
    {
        var block = new byte[16];
        Assert.Throws<ArgumentOutOfRangeException>(() => AstcBlockEncoder.Encode(new byte[7 * 7 * 4], 7, 7, srgb: true, block, thorough: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => AstcBlockEncoder.Encode(new byte[3 * 3 * 4], 3, 3, srgb: true, block, thorough: false));
        Assert.Throws<ArgumentException>(() => AstcBlockEncoder.Encode(new byte[(4 * 4 * 4) - 1], 4, 4, srgb: true, block, thorough: false));
        Assert.Throws<ArgumentException>(() => AstcBlockEncoder.Encode(new byte[4 * 4 * 4], 4, 4, srgb: true, new byte[15], thorough: false));
    }

    internal static (double Psnr, double[] MaxError) RoundTrip(string content, int blockWidth, int blockHeight, bool srgb, bool thorough)
    {
        var pixels = Image(content);
        var blocks = EncodeImage(pixels, ImageSize, ImageSize, blockWidth, blockHeight, srgb, thorough);
        return Compare(pixels, ImageSize, ImageSize, blocks, blockWidth, blockHeight, srgb);
    }

    /// <summary>The 48x48 test images: a crop of the photo-like fixture, a gradient of every channel with alpha, flat shapes with sharp edges.</summary>
    internal static byte[] Image(string content)
    {
        switch (content)
        {
            case "photo":
                var source = LoadFixture("jxl/lossy-rgb8-smooth", 96);
                var crop = new byte[ImageSize * ImageSize * 4];
                for (var y = 0; y < ImageSize; y++)
                {
                    source.AsSpan(((y + 16) * 96 * 4) + (24 * 4), ImageSize * 4).CopyTo(crop.AsSpan(y * ImageSize * 4));
                }

                return crop;
            case "gradient-alpha":
                return Gradient(ImageSize, ImageSize, alpha: true);
            case "edges":
                return Edges(ImageSize, ImageSize);
            default:
                throw new ArgumentOutOfRangeException(nameof(content));
        }
    }

    internal static byte[] Crafted(string content, int size)
    {
        var texels = new byte[size * size * 4];
        var step = 240 / size;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var third = x * 3 / size;
                var t = (x * 7) + y + 10;
                var (r, g, b, a) = content switch
                {
                    "gray-ramp" => (x * step, x * step, x * step, 255),
                    "gray-alpha" => (x * step, x * step, x * step, 255 - (y * step)),
                    "gray-alpha-same" => (x * step, x * step, x * step, x * step),
                    "two-colors" => x > y ? (230, 20, 30, 255) : (250, 240, 200, 255),
                    "rgba-line" => (x * step, 255 - (x * step), 100, 50 + (x * step * 3 / 4)),
                    "rgb-plane" => (x * step, y * step, 128, 255),
                    "rgba-alpha-indep" => (x * step, x * step * 3 / 4, x * step / 2, 255 - (y * step)),
                    "three-colors" => third == 0 ? (230, 20, 30, 255) : third == 1 ? (20, 200, 40, 255) : (30, 40, 220, 255),
                    "three-colors-rgba" => third == 0 ? (230, 20, 30, 255) : third == 1 ? (20, 200, 40, 128) : (30, 40, 220, 0),
                    "near-gray" => ((x * 40) + 2, x * 40, (x * 40) + 1, 255),
                    "scaled-rgba" => (t * 2, t, t / 2, 255 - ((t - 10) * 2)),
                    _ => throw new ArgumentOutOfRangeException(nameof(content)),
                };
                var offset = ((y * size) + x) * 4;
                texels[offset] = (byte)Math.Clamp(r, 0, 255);
                texels[offset + 1] = (byte)Math.Clamp(g, 0, 255);
                texels[offset + 2] = (byte)Math.Clamp(b, 0, 255);
                texels[offset + 3] = (byte)Math.Clamp(a, 0, 255);
            }
        }

        return texels;
    }

    /// <summary>Describes a block: "void" for a void extent, else the partitions, the endpoint mode and "d" for two planes.</summary>
    internal static string Describe(ReadOnlySpan<byte> block)
    {
        var bits = BinaryPrimitives.ReadUInt128LittleEndian(block);
        var mode = (int)(bits & 0x7FF);
        if ((mode & 0x1FF) == 0x1FC)
            return "void";

        Assert.True(AstcBlockDecoder.TryDecodeBlockMode(mode, out _, out _, out var dual, out _));
        var partitions = (int)((bits >> 11) & 3) + 1;
        var cem = partitions == 1 ? (int)((bits >> 13) & 0xF) : (int)((bits >> 25) & 0xF);
        if (partitions > 1)
        {
            // One endpoint mode shared by the partitions
            Assert.Equal(0, (int)((bits >> 23) & 3));
        }

        return $"p{partitions}c{cem}{(dual ? "d" : "")}";
    }

    private static byte[] Decode(byte[] block, int blockWidth, int blockHeight, AstcProfile profile)
    {
        var bytesPerTexel = profile == AstcProfile.Linear ? 8 : 4;
        var decoded = new byte[blockWidth * blockHeight * bytesPerTexel];
        AstcBlockDecoder.Decode(block, blockWidth, blockHeight, profile, decoded, blockWidth * bytesPerTexel);
        return decoded;
    }

    private static double MeanAbsoluteError(byte[] expected, byte[] actual)
    {
        var sum = 0.0;
        for (var i = 0; i < expected.Length; i++)
        {
            sum += Math.Abs(expected[i] - actual[i]);
        }

        return sum / expected.Length;
    }

    private static byte Next(ref ulong state)
    {
        state = (state * 6364136223846793005UL) + 1442695040888963407UL;
        return (byte)(state >> 56);
    }

    internal static byte[] LoadFixture(string id, int width)
    {
        var bytes = File.ReadAllBytes(FixtureRoot.GetDirectory() / (id + ".frame-0.rgba8.raw"));
        Assert.Equal(0, bytes.Length % (width * 4));
        return bytes;
    }

    internal static byte[] Gradient(int width, int height, bool alpha)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                pixels[offset] = (byte)(x * 255 / (width - 1));
                pixels[offset + 1] = (byte)(y * 255 / (height - 1));
                pixels[offset + 2] = (byte)((x + y) * 255 / (width + height - 2));
                pixels[offset + 3] = alpha ? (byte)(255 - ((x + (2 * y)) * 255 / (width + (2 * height) - 3))) : (byte)255;
            }
        }

        return pixels;
    }

    internal static byte[] Noise(int width, int height, ulong seed, bool alpha)
    {
        var pixels = new byte[width * height * 4];
        var state = seed;
        for (var i = 0; i < pixels.Length; i++)
        {
            var value = Next(ref state);
            pixels[i] = alpha || (i & 3) != 3 ? value : (byte)255;
        }

        return pixels;
    }

    internal static byte[] Edges(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                var inCircle = ((x - (width / 3)) * (x - (width / 3))) + ((y - (height * 5 / 12)) * (y - (height * 5 / 12))) < width * width / 16;
                var inBar = (x / 7) % 3 == 0 && y > height * 7 / 12;
                var diagonal = x > y + (width / 6);
                var (r, g, b) = inCircle ? (230, 20, 30) : inBar ? (10, 10, 10) : diagonal ? (40, 90, 220) : (250, 240, 200);
                pixels[offset] = (byte)r;
                pixels[offset + 1] = (byte)g;
                pixels[offset + 2] = (byte)b;
                pixels[offset + 3] = 255;
            }
        }

        return pixels;
    }

    /// <summary>Encodes an image block by block, the edge blocks padded by replicating the edge texels.</summary>
    internal static byte[] EncodeImage(byte[] pixels, int width, int height, int blockWidth, int blockHeight, bool srgb, bool thorough)
    {
        var columns = (width + blockWidth - 1) / blockWidth;
        var rows = (height + blockHeight - 1) / blockHeight;
        var blocks = new byte[columns * rows * 16];
        var texels = new byte[blockWidth * blockHeight * 4];
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                for (var y = 0; y < blockHeight; y++)
                {
                    for (var x = 0; x < blockWidth; x++)
                    {
                        var sx = Math.Min((column * blockWidth) + x, width - 1);
                        var sy = Math.Min((row * blockHeight) + y, height - 1);
                        pixels.AsSpan(((sy * width) + sx) * 4, 4).CopyTo(texels.AsSpan(((y * blockWidth) + x) * 4));
                    }
                }

                AstcBlockEncoder.Encode(texels, blockWidth, blockHeight, srgb, blocks.AsSpan(((row * columns) + column) * 16, 16), thorough);
            }
        }

        return blocks;
    }

    /// <summary>
    /// Decodes the blocks (8-bit results in the sRGB mode, the halves of decode_float16 otherwise) and returns the PSNR over
    /// the four channels and the maximum absolute error of each channel, in 8-bit units.
    /// </summary>
    internal static (double Psnr, double[] MaxError) Compare(byte[] pixels, int width, int height, byte[] blocks, int blockWidth, int blockHeight, bool srgb)
    {
        var columns = (width + blockWidth - 1) / blockWidth;
        var bytesPerTexel = srgb ? 4 : 8;
        var decoded = new byte[blockWidth * blockHeight * bytesPerTexel];
        var maxError = new double[4];
        var squared = 0.0;
        for (var row = 0; row * blockHeight < height; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                AstcBlockDecoder.Decode(blocks.AsSpan(((row * columns) + column) * 16, 16), blockWidth, blockHeight, srgb ? AstcProfile.Srgb : AstcProfile.Linear, decoded, blockWidth * bytesPerTexel);
                for (var y = 0; y < blockHeight && (row * blockHeight) + y < height; y++)
                {
                    for (var x = 0; x < blockWidth && (column * blockWidth) + x < width; x++)
                    {
                        var offset = ((((row * blockHeight) + y) * width) + (column * blockWidth) + x) * 4;
                        for (var c = 0; c < 4; c++)
                        {
                            var value = srgb ? decoded[(((y * blockWidth) + x) * 4) + c] : (double)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan((((y * blockWidth) + x) * 8) + (2 * c)))) * 255;
                            var error = Math.Abs(value - pixels[offset + c]);
                            maxError[c] = Math.Max(maxError[c], error);
                            squared += error * error;
                        }
                    }
                }
            }
        }

        var mse = squared / (width * height * 4);
        return (mse == 0 ? 99 : 10 * Math.Log10(255 * 255 / mse), maxError);
    }
}
