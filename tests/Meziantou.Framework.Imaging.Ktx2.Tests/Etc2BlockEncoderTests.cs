using System.Buffers.Binary;
using System.Security.Cryptography;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The ETC2 and EAC block encoders: every block decodes with <see cref="Etc2BlockDecoder"/> within the error measured for
/// each kind of content and level, solid values are exact where the format can store them, every mode is emitted, and the
/// output is the same on every platform.
/// </summary>
public sealed class Etc2BlockEncoderTests
{
    public enum Content
    {
        Gradient,
        Noise,
        Edges,
        Solid,
    }

    private const int BlockCount = 96;

    [Theory]
    [InlineData(147, Content.Gradient, false, 41.1, 17)]
    [InlineData(147, Content.Noise, false, 12.7, 173)]
    [InlineData(147, Content.Edges, false, 22.3, 91)]
    [InlineData(147, Content.Solid, false, 48.3, 2)]
    [InlineData(147, Content.Gradient, true, 41.1, 17)]
    [InlineData(147, Content.Noise, true, 14.0, 162)]
    [InlineData(147, Content.Edges, true, 37.3, 11)]
    [InlineData(147, Content.Solid, true, 48.7, 2)]
    [InlineData(149, Content.Gradient, false, 42.1, 20)]
    [InlineData(149, Content.Noise, false, 15.6, 251)]
    [InlineData(149, Content.Edges, false, 42.9, 6)]
    [InlineData(149, Content.Solid, false, 51.4, 2)]
    [InlineData(149, Content.Gradient, true, 42.2, 22)]
    [InlineData(149, Content.Noise, true, 17.3, 141)]
    [InlineData(149, Content.Edges, true, 43.4, 6)]
    [InlineData(149, Content.Solid, true, 51.8, 2)]
    [InlineData(151, Content.Gradient, false, 41.1, 17)]
    [InlineData(151, Content.Noise, false, 12.7, 173)]
    [InlineData(151, Content.Edges, false, 22.3, 91)]
    [InlineData(151, Content.Solid, false, 48.3, 2)]
    [InlineData(151, Content.Gradient, true, 41.1, 17)]
    [InlineData(151, Content.Noise, true, 14.0, 162)]
    [InlineData(151, Content.Edges, true, 37.3, 11)]
    [InlineData(151, Content.Solid, true, 48.7, 2)]
    public void ColorRoundTripStaysWithinTheMeasuredError(int format, Content content, bool thorough, double minPsnr, int maxError)
    {
        var (psnr, max, _) = MeasureColor(format, content, thorough);
        for (var channel = 0; channel < 3; channel++)
        {
            Assert.InRange(psnr[channel], minPsnr, double.PositiveInfinity);
            Assert.InRange(max[channel], 0, maxError);
        }

        if (format == 149)
        {
            // Every texel is exactly transparent or opaque, as requested
            Assert.Equal(0, max[3]);
        }
    }

    [Theory]
    [InlineData(Content.Gradient, false, 45.2, 6)]
    [InlineData(Content.Noise, false, 30.5, 26)]
    [InlineData(Content.Gradient, true, 45.4, 5)]
    [InlineData(Content.Noise, true, 30.7, 25)]
    public void AlphaRoundTripStaysWithinTheMeasuredError(Content content, bool thorough, double minPsnr, int maxError)
    {
        var (psnr, max, _) = MeasureColor(151, content, thorough);
        Assert.InRange(psnr[3], minPsnr, double.PositiveInfinity);
        Assert.InRange(max[3], 0, maxError);
    }

    [Theory]
    [InlineData(false, Content.Gradient, false, 45.1, 40)]
    [InlineData(false, Content.Noise, false, 30.6, 182)]
    [InlineData(false, Content.Edges, false, 51.9, 29)]
    [InlineData(false, Content.Solid, false, 76.5, 0.5)]
    [InlineData(false, Content.Gradient, true, 45.2, 40)]
    [InlineData(false, Content.Noise, true, 30.8, 164)]
    [InlineData(false, Content.Edges, true, 53.4, 25)]
    [InlineData(false, Content.Solid, true, 76.5, 0.5)]
    [InlineData(true, Content.Gradient, false, 45.0, 39)]
    [InlineData(true, Content.Noise, false, 30.6, 172)]
    [InlineData(true, Content.Edges, false, 51.8, 25)]
    [InlineData(true, Content.Solid, false, 76.2, 0.52)]
    [InlineData(true, Content.Gradient, true, 45.1, 39)]
    [InlineData(true, Content.Noise, true, 30.8, 167)]
    [InlineData(true, Content.Edges, true, 53.0, 26)]
    [InlineData(true, Content.Solid, true, 76.2, 0.52)]
    public void ElevenBitRoundTripStaysWithinTheMeasuredError(bool isSigned, Content content, bool thorough, double minPsnr, double maxError)
    {
        // In 11-bit units; a solid block is within half a unit: the nearest 11-bit value
        var (psnr, max, _) = MeasureEac(isSigned, content, thorough);
        Assert.InRange(psnr, minPsnr, double.PositiveInfinity);
        Assert.InRange(max, 0, maxError);
    }

    [Fact]
    public void ThoroughIsNeverWorse()
    {
        foreach (var content in Enum.GetValues<Content>())
        {
            foreach (var format in new[] { 147, 149, 151 })
            {
                var fast = MeasureColor(format, content, thorough: false).Psnr;
                var thorough = MeasureColor(format, content, thorough: true).Psnr;
                Assert.InRange(thorough.Take(3).Sum(Inverse), 0, fast.Take(3).Sum(Inverse) + 1e-12);
            }

            foreach (var isSigned in new[] { false, true })
            {
                Assert.InRange(MeasureEac(isSigned, content, thorough: true).Psnr, MeasureEac(isSigned, content, thorough: false).Psnr, double.PositiveInfinity);
            }
        }

        // The mean squared error is proportional to 10^(-PSNR/10)
        static double Inverse(double psnr) => double.IsPositiveInfinity(psnr) ? 0 : Math.Pow(10, -psnr / 10);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SolidColorsAreWithinTwo(bool thorough)
    {
        // The base colors have 4 or 5 bits and every channel takes the same intensity modifier; planar colors have 6 or 7
        var format = GetFormat(147);
        var seed = 5u;
        var block = new byte[64];
        var encoded = new byte[8];
        var decoded = new byte[64];
        for (var n = 0; n < 512; n++)
        {
            var color = n < 256 ? (uint)(n * 0x010101) : Next(ref seed);
            for (var i = 0; i < 16; i++)
            {
                block[i * 4] = (byte)color;
                block[(i * 4) + 1] = (byte)(color >> 8);
                block[(i * 4) + 2] = (byte)(color >> 16);
                block[(i * 4) + 3] = 255;
            }

            Etc2BlockEncoder.EncodeRgb(block, encoded, thorough);
            Etc2BlockDecoder.Decode(format, encoded, decoded, 16);
            for (var i = 0; i < 64; i++)
            {
                Assert.InRange(Math.Abs(decoded[i] - block[i]), 0, 2);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SolidValuesOfEacAreExact(bool thorough)
    {
        var block = new byte[64];
        var encoded = new byte[16];
        var decoded = new byte[256];

        // 8-bit alpha: the base codeword and a modifier of 0 (table 13)
        var rgba = GetFormat(151);
        for (var alpha = 0; alpha < 256; alpha++)
        {
            for (var i = 0; i < 16; i++)
            {
                block[(i * 4) + 3] = (byte)alpha;
            }

            Etc2BlockEncoder.EncodeRgba(block, encoded, thorough);
            Etc2BlockDecoder.Decode(rgba, encoded, decoded, 16);
            for (var i = 0; i < 16; i++)
            {
                Assert.Equal(alpha, decoded[(i * 4) + 3]);
            }
        }

        // Every 11-bit value, extended to 16 bits as the decoder does
        var r11 = GetFormat(153);
        var samples = new ushort[16];
        for (var value = 0; value < 2048; value++)
        {
            samples.AsSpan().Fill((ushort)((value << 5) | (value >> 6)));
            Etc2BlockEncoder.EncodeEacUnsigned(samples, 1, encoded, thorough);
            Etc2BlockDecoder.DecodeEac(r11, encoded, decoded, 64);
            for (var i = 0; i < 16; i++)
            {
                Assert.Equal(samples[0], BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan(((i >> 2) * 64) + ((i & 3) * 8))));
            }
        }

        // Every signed value, a multiple of 1/1023
        var signedR11 = GetFormat(154);
        var values = new float[16];
        for (var value = -1023; value <= 1023; value++)
        {
            values.AsSpan().Fill(value / 1023f);
            Etc2BlockEncoder.EncodeEacSigned(values, 1, encoded, thorough);
            Etc2BlockDecoder.DecodeEac(signedR11, encoded, decoded, 64);
            for (var i = 0; i < 16; i++)
            {
                Assert.Equal(value / 1023f, BinaryPrimitives.ReadSingleLittleEndian(decoded.AsSpan(((i >> 2) * 64) + ((i & 3) * 16))));
            }
        }
    }

    [Theory]
    [InlineData(float.NaN, 0)]
    [InlineData(float.PositiveInfinity, 1)]
    [InlineData(float.NegativeInfinity, -1)]
    [InlineData(5f, 1)]
    [InlineData(-5f, -1)]
    [InlineData(-0f, 0)]
    public void SignedValuesOutsideTheRangeAreClamped(float value, float expected)
    {
        var values = new float[16];
        values.AsSpan().Fill(value);
        var encoded = new byte[8];
        var decoded = new byte[256];
        foreach (var thorough in new[] { false, true })
        {
            Etc2BlockEncoder.EncodeEacSigned(values, 1, encoded, thorough);
            Etc2BlockDecoder.DecodeEac(GetFormat(154), encoded, decoded, 64);
            Assert.Equal(expected, BinaryPrimitives.ReadSingleLittleEndian(decoded));
        }
    }

    [Fact]
    public void EveryModeIsEmitted()
    {
        string[] Modes(int format, bool thorough)
        {
            var modes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var content in Enum.GetValues<Content>())
            {
                modes.UnionWith(MeasureColor(format, content, thorough).Modes);
            }

            return [.. modes.Order(StringComparer.Ordinal)];
        }

        // The fast level has the modes of ETC1 and planar; the thorough level T and H too. Punchthrough alpha has no
        // individual mode: its bit is the opaque bit
        Assert.Equal(["Differential", "Individual", "Planar"], Modes(147, thorough: false));
        Assert.Equal(["Differential", "H", "Individual", "Planar", "T"], Modes(147, thorough: true));
        Assert.Equal(["Differential", "Planar"], Modes(149, thorough: false));
        Assert.Equal(["Differential", "H", "Planar", "T"], Modes(149, thorough: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CraftedBlocksUseTheirMode(bool thorough)
    {
        var encoded = new byte[8];

        // Shades of red and of blue, the 4-bit colors (204, 0, 0) and (0, 0, 170) plus the modifiers of table 0, one per
        // row (clamped): too far apart for a delta, and eight colors for the four paints of T and H
        var block = new byte[64];
        for (var i = 0; i < 16; i++)
        {
            var modifier = (i >> 2) switch { 0 => -8, 1 => -2, 2 => 2, _ => 8 };
            var left = (i & 3) < 2;
            block[i * 4] = (byte)Math.Max((left ? 204 : 0) + modifier, 0);
            block[(i * 4) + 1] = (byte)Math.Max(modifier, 0);
            block[(i * 4) + 2] = (byte)Math.Max((left ? 0 : 170) + modifier, 0);
            block[(i * 4) + 3] = 255;
        }

        Etc2BlockEncoder.EncodeRgb(block, encoded, thorough);
        Assert.Equal("Individual", GetMode(encoded, punchthrough: false));
        AssertDecodes(block, encoded, 0);

        // A smooth ramp: planar, within the rounding of its 6- and 7-bit colors
        for (var i = 0; i < 16; i++)
        {
            block[i * 4] = (byte)(40 + (20 * (i & 3)));
            block[(i * 4) + 1] = (byte)(200 - (12 * (i >> 2)));
            block[(i * 4) + 2] = (byte)(100 + (8 * (i & 3)) + (8 * (i >> 2)));
        }

        Etc2BlockEncoder.EncodeRgb(block, encoded, thorough);
        Assert.Equal("Planar", GetMode(encoded, punchthrough: false));
        AssertDecodes(block, encoded, 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PunchthroughSetsTheOpaqueBitOfOpaqueBlocksOnly(bool thorough)
    {
        var format = GetFormat(149);
        var seed = 3u;
        var encoded = new byte[8];
        var decoded = new byte[64];
        for (var n = 0; n < 64; n++)
        {
            // No transparent texel, some, or all of them
            var block = CreateBlock(Content.Noise, ref seed);
            for (var i = 0; i < 16; i++)
            {
                block[(i * 4) + 3] = (n % 3) switch
                {
                    0 => (byte)(128 + (block[(i * 4) + 3] & 127)),
                    1 => block[(i * 4) + 3],
                    _ => (byte)(block[(i * 4) + 3] & 127),
                };
            }

            Etc2BlockEncoder.EncodePunchthrough(block, encoded, thorough);
            Etc2BlockDecoder.Decode(format, encoded, decoded, 16);
            var opaque = true;
            for (var i = 0; i < 16; i++)
            {
                var transparent = block[(i * 4) + 3] < 128;
                opaque &= !transparent;
                Assert.Equal(transparent ? 0 : 255, decoded[(i * 4) + 3]);
            }

            Assert.Equal(opaque, (encoded[3] & 2) != 0);
        }
    }

    [Fact]
    public void ElevenBitFlatBlocksUseTheMultiplierZero()
    {
        // A multiplier of 0 is 1/8: the modifiers are 11-bit steps. The 8-bit alpha block never uses it
        var unsignedMultipliers = new HashSet<int>();
        var signedMultipliers = new HashSet<int>();
        foreach (var content in Enum.GetValues<Content>())
        {
            unsignedMultipliers.UnionWith(MeasureEac(isSigned: false, content, thorough: false).Multipliers);
            signedMultipliers.UnionWith(MeasureEac(isSigned: true, content, thorough: false).Multipliers);
        }

        Assert.Contains(0, unsignedMultipliers);
        Assert.Contains(0, signedMultipliers);

        var seed = 8u;
        var encoded = new byte[16];
        for (var n = 0; n < 256; n++)
        {
            Etc2BlockEncoder.EncodeRgba(CreateBlock((Content)(n % 4), ref seed), encoded, thorough: (n & 4) != 0);
            Assert.NotEqual(0, encoded[1] >> 4);
        }
    }

    [Theory]
    [InlineData(147, false, "5d9411dfe891e4d2f3e39cb25ea7801c1627b6b0a6eb185239e3ed123a3ff384")]
    [InlineData(149, false, "09219712c85be3daf4d26ade1bacbfea91eeaafefd452cd4bb59d98a5de7ac81")]
    [InlineData(151, false, "b66f8556572b373b6419272a611b8bfd2e7b78e3962b3b3a9ec21464789aea6d")]
    [InlineData(155, false, "72e4f0a8c786cbf7a9f9d7f8e2b3a2ec075e8d83348211421701a970b4e7e9fa")]
    [InlineData(156, false, "ff64695aec6db9ed1b070b4775d3978cef963631178524e591c0c0b556bd8abc")]
    [InlineData(147, true, "ef4476896414d6e066d9b7a69c168bc5890a1a7506a06d54b4cc6835d4af055d")]
    [InlineData(149, true, "9aa2270f9d98bc25427f503406eccbeaec13d0d038340472483a8b5c106c3728")]
    [InlineData(151, true, "e54046f21cd9e0a505b61baf556916ab2ad800c974b4dd0c316cac21ae7ec1b7")]
    [InlineData(155, true, "c176b70fa0e95bf793d353d22e9632e9acef9b829fdddf7e5a93000efe49ba77")]
    [InlineData(156, true, "37409f679884374f9fd6d2dcbd0959a96c144ca413ed68f1c4ed77e2e4dfed7e")]
    public void OutputIsTheSameOnEveryPlatform(int format, bool thorough, string expected)
    {
        Assert.Equal(expected, ComputeHash(format, thorough));
    }

    private static void AssertDecodes(byte[] block, byte[] encoded, int maxError)
    {
        var decoded = new byte[64];
        Etc2BlockDecoder.Decode(GetFormat(147), encoded, decoded, 16);
        for (var i = 0; i < 64; i++)
        {
            Assert.InRange(Math.Abs(decoded[i] - block[i]), 0, maxError);
        }
    }

    private static Ktx2VkFormat GetFormat(int value)
    {
        Assert.True(Ktx2VkFormats.TryGet(value, out var format));
        return format;
    }

    /// <summary>A small linear congruential generator, so that the content is the same on every platform.</summary>
    private static uint Next(ref uint seed)
    {
        seed = (seed * 1664525) + 1013904223;
        return seed >> 8;
    }

    /// <summary>Creates a block of 16 RGBA texels, row by row.</summary>
    internal static byte[] CreateBlock(Content content, ref uint seed)
    {
        var block = new byte[64];
        switch (content)
        {
            case Content.Gradient:
                {
                    // A smooth ramp in each channel, alpha included
                    Span<int> origin = stackalloc int[4];
                    Span<int> slopeX = stackalloc int[4];
                    Span<int> slopeY = stackalloc int[4];
                    for (var channel = 0; channel < 4; channel++)
                    {
                        origin[channel] = (int)(Next(ref seed) % 256);
                        slopeX[channel] = (int)(Next(ref seed) % 25) - 12;
                        slopeY[channel] = (int)(Next(ref seed) % 25) - 12;
                    }

                    for (var i = 0; i < 16; i++)
                    {
                        for (var channel = 0; channel < 4; channel++)
                        {
                            block[(i * 4) + channel] = (byte)Math.Clamp(origin[channel] + (slopeX[channel] * (i & 3)) + (slopeY[channel] * (i >> 2)), 0, 255);
                        }
                    }

                    break;
                }

            case Content.Noise:
                for (var i = 0; i < 64; i++)
                {
                    block[i] = (byte)Next(ref seed);
                }

                break;

            case Content.Edges:
                {
                    // Two colors on either side of a straight edge
                    var color1 = Next(ref seed);
                    var color2 = Next(ref seed);
                    var vertical = (Next(ref seed) & 1) != 0;
                    var position = (int)(Next(ref seed) % 3) + 1;
                    for (var i = 0; i < 16; i++)
                    {
                        var color = (vertical ? i & 3 : i >> 2) < position ? color1 : color2;
                        block[i * 4] = (byte)color;
                        block[(i * 4) + 1] = (byte)(color >> 8);
                        block[(i * 4) + 2] = (byte)(color >> 16);
                        block[(i * 4) + 3] = color == color1 ? (byte)255 : (byte)0;
                    }

                    break;
                }

            default:
                {
                    var color = Next(ref seed);
                    var alpha = (byte)Next(ref seed);
                    for (var i = 0; i < 16; i++)
                    {
                        block[i * 4] = (byte)color;
                        block[(i * 4) + 1] = (byte)(color >> 8);
                        block[(i * 4) + 2] = (byte)(color >> 16);
                        block[(i * 4) + 3] = alpha;
                    }

                    break;
                }
        }

        return block;
    }

    /// <summary>Gets the mode of an ETC2 color block, as the decoder reads it (section 22.1).</summary>
    internal static string GetMode(ReadOnlySpan<byte> block, bool punchthrough)
    {
        var high = BinaryPrimitives.ReadUInt32BigEndian(block);
        if (!punchthrough && (high & 2) == 0)
            return "Individual";

        if (Overflows(high >> 27, high >> 24))
            return "T";

        if (Overflows(high >> 19, high >> 16))
            return "H";

        return Overflows(high >> 11, high >> 8) ? "Planar" : "Differential";

        static bool Overflows(uint value, uint delta) => (int)(value & 0x1F) + (((int)(delta & 7) << 29) >> 29) is < 0 or > 31;
    }

    /// <summary>Encodes and decodes blocks of a content; returns the PSNR of each channel (RGBA) and their largest error.</summary>
    internal static (double[] Psnr, int[] MaxError, HashSet<string> Modes) MeasureColor(int formatValue, Content content, bool thorough)
    {
        var format = GetFormat(formatValue);
        var squares = new double[4];
        var maxError = new int[4];
        var modes = new HashSet<string>(StringComparer.Ordinal);
        var seed = (uint)content + 1;
        var encoded = new byte[format.BytesPerBlock];
        var decoded = new byte[64];
        for (var n = 0; n < BlockCount; n++)
        {
            var block = CreateBlock(content, ref seed);
            switch (formatValue)
            {
                case 147:
                    Etc2BlockEncoder.EncodeRgb(block, encoded, thorough);
                    break;
                case 149:
                    Etc2BlockEncoder.EncodePunchthrough(block, encoded, thorough);
                    break;
                default:
                    Etc2BlockEncoder.EncodeRgba(block, encoded, thorough);
                    break;
            }

            Etc2BlockDecoder.Decode(format, encoded, decoded, 16);
            modes.Add(GetMode(encoded.AsSpan(formatValue == 151 ? 8 : 0), formatValue == 149));
            for (var i = 0; i < 16; i++)
            {
                var transparent = formatValue == 149 && block[(i * 4) + 3] < 128;
                for (var channel = 0; channel < 4; channel++)
                {
                    int expected = block[(i * 4) + channel];
                    if (channel == 3 && formatValue != 151)
                    {
                        // Punchthrough alpha is exactly 0 or 255
                        expected = transparent ? 0 : 255;
                    }
                    else if (transparent)
                    {
                        expected = 0; // Transparent black
                    }

                    var error = Math.Abs(decoded[(i * 4) + channel] - expected);
                    squares[channel] += error * error;
                    maxError[channel] = Math.Max(maxError[channel], error);
                }
            }
        }

        var psnr = new double[4];
        for (var channel = 0; channel < 4; channel++)
        {
            psnr[channel] = squares[channel] == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255 * BlockCount * 16 / squares[channel]);
        }

        return (psnr, maxError, modes);
    }

    /// <summary>Encodes and decodes the red channel of blocks as R11: returns the PSNR and the largest error, in 11-bit units.</summary>
    internal static (double Psnr, double MaxError, HashSet<int> Multipliers) MeasureEac(bool isSigned, Content content, bool thorough)
    {
        var format = GetFormat(isSigned ? 154 : 153);
        double squares = 0, maxError = 0;
        var multipliers = new HashSet<int>();
        var seed = (uint)content + 10;
        var unsignedSamples = new ushort[16];
        var signedSamples = new float[16];
        var encoded = new byte[8];
        var decoded = new byte[256];
        for (var n = 0; n < BlockCount; n++)
        {
            // 16-bit samples: red, and green as the low byte
            var block = CreateBlock(content, ref seed);
            for (var i = 0; i < 16; i++)
            {
                unsignedSamples[i] = (ushort)((block[i * 4] << 8) | block[(i * 4) + 1]);
                signedSamples[i] = (unsignedSamples[i] / 32767.5f) - 1;
            }

            if (isSigned)
            {
                Etc2BlockEncoder.EncodeEacSigned(signedSamples, 1, encoded, thorough);
            }
            else
            {
                Etc2BlockEncoder.EncodeEacUnsigned(unsignedSamples, 1, encoded, thorough);
            }

            multipliers.Add(encoded[1] >> 4);
            Etc2BlockDecoder.DecodeEac(format, encoded, decoded, 64);
            for (var i = 0; i < 16; i++)
            {
                var (x, y) = (i & 3, i >> 2);
                var error = isSigned
                    ? (BinaryPrimitives.ReadSingleLittleEndian(decoded.AsSpan((y * 64) + (x * 16))) - signedSamples[i]) * 1023.0
                    : (BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan((y * 64) + (x * 8))) - unsignedSamples[i]) * 2047.0 / 65535;
                squares += error * error;
                maxError = Math.Max(maxError, Math.Abs(error));
            }
        }

        var peak = isSigned ? 2046.0 : 2047.0;
        return (squares == 0 ? double.PositiveInfinity : 10 * Math.Log10(peak * peak * BlockCount * 16 / squares), maxError, multipliers);
    }

    /// <summary>
    /// Encodes blocks of every content in a format; returns the SHA-256 of the blocks. The EAC formats encode two channels
    /// interleaved (stride 2), as RG11 does.
    /// </summary>
    internal static string ComputeHash(int formatValue, bool thorough)
    {
        var seed = 2024u;
        var output = new List<byte>();
        var encoded = new byte[16];
        var unsignedSamples = new ushort[32];
        var signedSamples = new float[32];
        for (var n = 0; n < 64; n++)
        {
            var block = CreateBlock((Content)(n % 4), ref seed);
            for (var i = 0; i < 32; i++)
            {
                unsignedSamples[i] = (ushort)((block[i * 2] << 8) | block[(i * 2) + 1]);
                signedSamples[i] = (unsignedSamples[i] / 32767.5f) - 1;
            }

            switch (formatValue)
            {
                case 147:
                    Etc2BlockEncoder.EncodeRgb(block, encoded, thorough);
                    output.AddRange(encoded.AsSpan(0, 8));
                    break;
                case 149:
                    Etc2BlockEncoder.EncodePunchthrough(block, encoded, thorough);
                    output.AddRange(encoded.AsSpan(0, 8));
                    break;
                case 151:
                    Etc2BlockEncoder.EncodeRgba(block, encoded, thorough);
                    output.AddRange(encoded);
                    break;
                case 155:
                    Etc2BlockEncoder.EncodeEacUnsigned(unsignedSamples, 2, encoded, thorough);
                    Etc2BlockEncoder.EncodeEacUnsigned(unsignedSamples.AsSpan(1), 2, encoded.AsSpan(8), thorough);
                    output.AddRange(encoded);
                    break;
                default:
                    Etc2BlockEncoder.EncodeEacSigned(signedSamples, 2, encoded, thorough);
                    Etc2BlockEncoder.EncodeEacSigned(signedSamples.AsSpan(1), 2, encoded.AsSpan(8), thorough);
                    output.AddRange(encoded);
                    break;
            }
        }

        return Convert.ToHexStringLower(SHA256.HashData(output.ToArray()));
    }
}
