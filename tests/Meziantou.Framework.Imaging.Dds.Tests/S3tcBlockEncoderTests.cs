using System.Buffers.Binary;
using System.Security.Cryptography;
using Meziantou.Framework.Imaging.Internals;
using static Meziantou.Framework.Imaging.Tests.BlockEncoderTestImages;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The S3TC (BC1, BC2, BC3) and RGTC (BC4, BC5) block encoders, through the decoders of <see cref="BlockDecoder"/>: the
/// error bounds measured on each kind of content at both levels, the blocks of one color, the modes and palettes chosen
/// for crafted blocks, extreme inputs and the determinism of the encoded bytes.
/// </summary>
public sealed class S3tcBlockEncoderTests
{
    private delegate void RoundTrip(ReadOnlySpan<byte> texels, Span<byte> decoded);

    [Theory]
    [InlineData("photo", false, 29.8, 43)]
    [InlineData("photo", true, 29.9, 43)]
    [InlineData("gradient", false, 38.5, 10)]
    [InlineData("gradient", true, 38.6, 10)]
    [InlineData("noise", false, 13.6, 185)]
    [InlineData("noise", true, 13.7, 185)]
    [InlineData("edges", false, 41.6, 5)]
    [InlineData("edges", true, 42.0, 5)]
    [InlineData("solid", false, 51.4, 1)]
    [InlineData("solid", true, 51.4, 1)]
    [InlineData("noise+alpha", false, 13.6, 185)]
    public void Bc1OpaqueRoundTripStaysWithinBounds(string name, bool thorough, double minimumPsnr, int maximumError)
    {
        var image = Get(name);
        var (psnr, error) = Measure(image, channels: 3, (texels, decoded) =>
        {
            Span<byte> block = stackalloc byte[8];
            S3tcBlockEncoder.EncodeBc1(texels, block, allowTransparency: false, thorough);
            BlockDecoder.DecodeBc1(block, decoded);
            for (var i = 0; i < 16; i++)
            {
                // Alpha is ignored: no texel may use the transparent entry of the three-color mode
                Assert.Equal(255, decoded[(i * 4) + 3]);
            }
        });

        Assert.InRange(psnr, minimumPsnr, double.PositiveInfinity, message: name);
        Assert.InRange(error, 0, maximumError, message: name);
    }

    [Theory]
    [InlineData("photo+alpha", false, 29.8, 43)]
    [InlineData("photo+alpha", true, 29.9, 43)]
    [InlineData("noise+alpha", false, 14.4, 160)]
    [InlineData("noise+alpha", true, 14.5, 160)]
    [InlineData("alpha-gradient", false, 38.0, 10)]
    [InlineData("alpha-gradient", true, 38.8, 10)]
    [InlineData("cutout", false, 37.8, 8)]
    [InlineData("cutout", true, 38.2, 9)]
    [InlineData("solid+alpha", false, 51.3, 1)]
    public void Bc1TransparencyIsExactAndColorsStayWithinBounds(string name, bool thorough, double minimumPsnr, int maximumError)
    {
        var image = Get(name);
        Span<byte> texels = stackalloc byte[64];
        Span<byte> decoded = stackalloc byte[64];
        Span<byte> block = stackalloc byte[8];
        double squaredError = 0;
        var count = 0;
        var maximum = 0;
        for (var index = 0; index < image.BlockCount; index++)
        {
            image.CopyBlock(index, texels);
            S3tcBlockEncoder.EncodeBc1(texels, block, allowTransparency: true, thorough);
            BlockDecoder.DecodeBc1(block, decoded);
            for (var i = 0; i < 16; i++)
            {
                if (texels[(i * 4) + 3] < 128)
                {
                    Assert.Equal(0, decoded[(i * 4) + 3]);
                    continue;
                }

                Assert.Equal(255, decoded[(i * 4) + 3]);
                for (var c = 0; c < 3; c++)
                {
                    var difference = decoded[(i * 4) + c] - texels[(i * 4) + c];
                    squaredError += difference * difference;
                    maximum = Math.Max(maximum, Math.Abs(difference));
                }

                count += 3;
            }
        }

        Assert.InRange(Psnr(squaredError, count), minimumPsnr, double.PositiveInfinity, message: name);
        Assert.InRange(maximum, 0, maximumError, message: name);
    }

    [Theory]
    [InlineData("photo+alpha", false, 30.4, 43)]
    [InlineData("photo+alpha", true, 30.5, 43)]
    [InlineData("alpha-gradient", false, 36.4, 10)]
    [InlineData("alpha-gradient", true, 37.0, 10)]
    [InlineData("noise+alpha", false, 14.8, 170)]
    [InlineData("noise+alpha", true, 14.8, 170)]
    [InlineData("solid+alpha", false, 40.2, 8)]
    public void Bc2RoundTripStaysWithinBounds(string name, bool thorough, double minimumPsnr, int maximumError)
    {
        var (psnr, error) = Measure(Get(name), channels: 4, (texels, decoded) =>
        {
            Span<byte> block = stackalloc byte[16];
            S3tcBlockEncoder.EncodeBc2(texels, block, thorough);
            BlockDecoder.DecodeBc2(block, decoded);
            for (var i = 0; i < 16; i++)
            {
                // 4 bits of alpha: the nearest multiple of 17
                Assert.InRange(Math.Abs(decoded[(i * 4) + 3] - texels[(i * 4) + 3]), 0, 8);
            }
        });

        Assert.InRange(psnr, minimumPsnr, double.PositiveInfinity, message: name);
        Assert.InRange(error, 0, maximumError, message: name);
    }

    [Theory]
    [InlineData("photo+alpha", false, 30.8, 43)]
    [InlineData("photo+alpha", true, 31.0, 43)]
    [InlineData("alpha-gradient", false, 38.6, 10)]
    [InlineData("alpha-gradient", true, 39.9, 10)]
    [InlineData("noise+alpha", false, 14.7, 170)]
    [InlineData("noise+alpha", true, 14.8, 170)]
    [InlineData("solid+alpha", false, 52.1, 1)]
    [InlineData("cutout", true, 39.3, 9)]
    public void Bc3RoundTripStaysWithinBounds(string name, bool thorough, double minimumPsnr, int maximumError)
    {
        var (psnr, error) = Measure(Get(name), channels: 4, (texels, decoded) =>
        {
            Span<byte> block = stackalloc byte[16];
            S3tcBlockEncoder.EncodeBc3(texels, block, thorough);
            BlockDecoder.DecodeBc3(block, decoded);
        });

        Assert.InRange(psnr, minimumPsnr, double.PositiveInfinity, message: name);
        Assert.InRange(error, 0, maximumError, message: name);
    }

    [Theory]
    [InlineData("photo", false, 39.9, 6)]
    [InlineData("photo", true, 41.6, 6)]
    [InlineData("gradient", false, 51.0, 1)]
    [InlineData("gradient", true, double.PositiveInfinity, 0)]
    [InlineData("noise", false, 29.2, 23)]
    [InlineData("noise", true, 30.4, 21)]
    [InlineData("edges", false, double.PositiveInfinity, 0)]
    [InlineData("cutout", true, double.PositiveInfinity, 0)]
    public void UnsignedChannelRoundTripStaysWithinBounds(string name, bool thorough, double minimumPsnr, int maximumError)
    {
        var (psnr, error) = Measure(Get(name), channels: 1, (texels, decoded) =>
        {
            Span<byte> block = stackalloc byte[8];
            S3tcBlockEncoder.EncodeUnsignedChannel(texels, 4, block, thorough);
            BlockDecoder.DecodeUnsignedChannel(block, decoded, 4);
        });

        Assert.InRange(psnr, minimumPsnr, double.PositiveInfinity, message: name);
        Assert.InRange(error, 0, maximumError, message: name);
    }

    [Theory]
    [InlineData("photo", false, 39.9, 6)]
    [InlineData("photo", true, 41.6, 6)]
    [InlineData("gradient", false, 51.0, 1)]
    [InlineData("gradient", true, double.PositiveInfinity, 0)]
    [InlineData("noise", false, 29.2, 23)]
    [InlineData("noise", true, 30.4, 21)]
    public void SignedChannelRoundTripStaysWithinBounds(string name, bool thorough, double minimumPsnr, int maximumError)
    {
        // The red channel mapped to -1..1 (255 is above 1 and clamped): the error is in steps of 1/127
        var (psnr, error) = Measure(Get(name), channels: 1, (texels, decoded) =>
        {
            Span<float> samples = stackalloc float[16];
            Span<float> decodedSamples = stackalloc float[16];
            Span<byte> block = stackalloc byte[8];
            for (var i = 0; i < 16; i++)
            {
                samples[i] = (texels[i * 4] - 127) / 127f;
            }

            S3tcBlockEncoder.EncodeSignedChannel(samples, 1, block, thorough);
            BlockDecoder.DecodeSignedChannel(block, decodedSamples, 1);
            Assert.NotEqual(0x80, block[0]);
            Assert.NotEqual(0x80, block[1]);
            for (var i = 0; i < 16; i++)
            {
                // Steps of 1/127 from 0 (-1) to 254 (1); 255 is above 1, so its reference is 1: the decoded 254 is shifted to
                // 255 for it
                decoded[i * 4] = (byte)((int)Math.Round(decodedSamples[i] * 127, MidpointRounding.AwayFromZero) + 127 + (texels[i * 4] == 255 ? 1 : 0));
            }
        });

        Assert.InRange(psnr, minimumPsnr, double.PositiveInfinity, message: name);
        Assert.InRange(error, 0, maximumError, message: name);
    }

    [Fact]
    public void SolidColorsUseTheNearestEntryOfTheBestMode()
    {
        // The values each channel can decode to, from the formulas of section 18.1: the endpoints and the entries at 1/3 and
        // 2/3 (four-color mode), or the endpoints and the entry at 1/2 (three-color mode)
        var four5 = ReachableValues(31, threeColorMode: false);
        var four6 = ReachableValues(63, threeColorMode: false);
        var three5 = ReachableValues(31, threeColorMode: true);
        var three6 = ReachableValues(63, threeColorMode: true);
        Span<byte> texels = stackalloc byte[64];
        Span<byte> decoded = stackalloc byte[64];
        Span<byte> block = stackalloc byte[16];
        var random = new Lcg(7);
        for (var sample = 0; sample < 768; sample++)
        {
            int r, g, b;
            if (sample < 256)
            {
                r = g = b = sample;
            }
            else if (sample < 512)
            {
                (r, g, b) = (sample - 256, 0, 511 - sample);
            }
            else
            {
                (r, g, b) = (random.Next(), random.Next(), random.Next());
            }

            for (var i = 0; i < 16; i++)
            {
                texels[i * 4] = (byte)r;
                texels[(i * 4) + 1] = (byte)g;
                texels[(i * 4) + 2] = (byte)b;
                texels[(i * 4) + 3] = 200;
            }

            var fourError = Nearest(four5, r) + Nearest(four6, g) + Nearest(four5, b);
            var threeError = Nearest(three5, r) + Nearest(three6, g) + Nearest(three5, b);
            foreach (var thorough in new[] { false, true })
            {
                S3tcBlockEncoder.EncodeBc1(texels, block, allowTransparency: false, thorough);
                BlockDecoder.DecodeBc1(block, decoded);
                Assert.Equal(Math.Min(fourError, threeError), SquaredError(texels, decoded, 3));

                S3tcBlockEncoder.EncodeBc3(texels, block, thorough);
                BlockDecoder.DecodeBc3(block, decoded);
                Assert.Equal(fourError, SquaredError(texels, decoded, 3));
                Assert.Equal(200, decoded[3]);
            }
        }

        // Every 8-bit value is within 1 of an entry of both bit depths
        for (var value = 0; value < 256; value++)
        {
            Assert.InRange(Nearest(four5, value), 0, 1);
            Assert.InRange(Nearest(four6, value), 0, 1);
        }
    }

    [Fact]
    public void Bc1UsesTheThreeColorModeWithoutTransparencyWhenItIsBetter()
    {
        // Black, white and the gray between them: the half of the three-color mode is exact, the thirds are not
        var texels = Block(i => (i % 3) switch
        {
            0 => (0, 0, 0, 0),
            1 => (255, 255, 255, 0),
            _ => (128, 128, 128, 0),
        });

        foreach (var thorough in new[] { false, true })
        {
            var block = new byte[8];
            S3tcBlockEncoder.EncodeBc1(texels, block, allowTransparency: false, thorough);
            Assert.InRange((int)BinaryPrimitives.ReadUInt16LittleEndian(block), 0, BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(2)));
            var decoded = new byte[64];
            BlockDecoder.DecodeBc1(block, decoded);
            for (var i = 0; i < 16; i++)
            {
                Assert.Equal(255, decoded[(i * 4) + 3]);
                Assert.Equal(texels[i * 4], decoded[i * 4]);
            }
        }
    }

    [Fact]
    public void Bc1UsesTheFourColorModeForARamp()
    {
        var texels = Block(i => (i * 17, i * 17, i * 17, 255));
        foreach (var thorough in new[] { false, true })
        {
            var block = new byte[8];
            S3tcBlockEncoder.EncodeBc1(texels, block, allowTransparency: true, thorough);
            Assert.InRange((int)BinaryPrimitives.ReadUInt16LittleEndian(block), BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(2)) + 1, ushort.MaxValue);
        }
    }

    [Fact]
    public void Bc1AlphaThresholdIs128()
    {
        var texels = Block(i => (i * 16, 100, 50, i % 2 == 0 ? 127 : 128));
        var block = new byte[8];
        S3tcBlockEncoder.EncodeBc1(texels, block, allowTransparency: true, thorough: false);
        var decoded = new byte[64];
        BlockDecoder.DecodeBc1(block, decoded);
        for (var i = 0; i < 16; i++)
        {
            Assert.Equal(i % 2 == 0 ? 0 : 255, decoded[(i * 4) + 3]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bc1OfTransparentTexelsOnlyDecodesTransparent(bool thorough)
    {
        var texels = Block(i => (i * 16, 255 - (i * 16), 7, 0));
        var block = new byte[8];
        S3tcBlockEncoder.EncodeBc1(texels, block, allowTransparency: true, thorough);
        var decoded = new byte[64];
        BlockDecoder.DecodeBc1(block, decoded);
        Assert.Equal(new byte[64], decoded);
    }

    [Fact]
    public void Bc2AlphaIsRoundedToTheNearestLevel()
    {
        var block = new byte[16];
        var decoded = new byte[64];
        for (var start = 0; start < 256; start += 16)
        {
            var texels = Block(i => (0, 0, 0, start + i));
            S3tcBlockEncoder.EncodeBc2(texels, block, thorough: false);
            BlockDecoder.DecodeBc2(block, decoded);
            for (var i = 0; i < 16; i++)
            {
                Assert.Equal((int)Math.Round((start + i) / 17.0, MidpointRounding.AwayFromZero) * 17, decoded[(i * 4) + 3]);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsignedChannelUsesTheEightValuePaletteForEvenlySpacedValues(bool thorough)
    {
        // 10 to 220 by 30: the sevenths between 220 and 10, exactly
        var samples = new byte[16];
        for (var i = 0; i < 16; i++)
        {
            samples[i] = (byte)(10 + ((i % 8) * 30));
        }

        var block = new byte[8];
        S3tcBlockEncoder.EncodeUnsignedChannel(samples, 1, block, thorough);
        Assert.InRange((int)block[0], block[1] + 1, 255);
        var decoded = new byte[16];
        BlockDecoder.DecodeUnsignedChannel(block, decoded, 1);
        Assert.Equal(samples, decoded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsignedChannelUsesTheSixValuePaletteForTheExtremes(bool thorough)
    {
        // 0 and 255 are the extra entries; 100 to 120 by 4 are the fifths between the endpoints
        byte[] samples = [0, 255, 100, 104, 108, 112, 116, 120, 0, 255, 100, 104, 108, 112, 116, 120];
        var block = new byte[8];
        S3tcBlockEncoder.EncodeUnsignedChannel(samples, 1, block, thorough);
        Assert.InRange((int)block[0], 0, block[1]);
        var decoded = new byte[16];
        BlockDecoder.DecodeUnsignedChannel(block, decoded, 1);
        Assert.Equal(samples, decoded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignedChannelClampsAndNeverWritesMinus128(bool thorough)
    {
        float[] samples = [float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1, 1, 2, -2, 0, 0.5f, -0.5f, 1e-30f, -1e-30f, float.MaxValue, float.MinValue, 0.25f, -0.75f];
        var block = new byte[8];
        S3tcBlockEncoder.EncodeSignedChannel(samples, 1, block, thorough);
        Assert.NotEqual(0x80, block[0]);
        Assert.NotEqual(0x80, block[1]);
        var decoded = new float[16];
        BlockDecoder.DecodeSignedChannel(block, decoded, 1);
        for (var i = 0; i < 16; i++)
        {
            var expected = float.IsNaN(samples[i]) ? 0 : Math.Clamp(samples[i], -1, 1);
            Assert.Equal(expected, decoded[i], 0.2f);
        }

        // One value: exact, including both extremes
        foreach (var value in new[] { -1f, 1f, 0f, float.NaN, -0.5f })
        {
            Array.Fill(samples, value);
            S3tcBlockEncoder.EncodeSignedChannel(samples, 1, block, thorough);
            BlockDecoder.DecodeSignedChannel(block, decoded, 1);
            var expected = float.IsNaN(value) ? 0 : (float)(Math.Round(value * 127, MidpointRounding.AwayFromZero) / 127);
            Assert.Equal(Enumerable.Repeat(expected, 16).ToArray(), decoded);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    public void ExtremeValuesAreExact(int value)
    {
        var texels = Block(i => (value, value, value, value));
        foreach (var thorough in new[] { false, true })
        {
            var block = new byte[16];
            var decoded = new byte[64];
            S3tcBlockEncoder.EncodeBc1(texels, block, allowTransparency: false, thorough);
            BlockDecoder.DecodeBc1(block, decoded);
            Assert.Equal(Block(i => (value, value, value, 255)), decoded);

            S3tcBlockEncoder.EncodeBc2(texels, block, thorough);
            BlockDecoder.DecodeBc2(block, decoded);
            Assert.Equal(texels, decoded);

            S3tcBlockEncoder.EncodeBc3(texels, block, thorough);
            BlockDecoder.DecodeBc3(block, decoded);
            Assert.Equal(texels, decoded);

            S3tcBlockEncoder.EncodeUnsignedChannel(texels, 4, block, thorough);
            BlockDecoder.DecodeUnsignedChannel(block, decoded, 4);
            Assert.Equal(texels, decoded);
        }
    }

    [Theory]
    [InlineData("BC1", false, "100def654881cd950a55b435a08ad5ff363d993affba9aff000198df65332b69")]
    [InlineData("BC1", true, "26113f3db4e39c35dc33c44081a63d974671f948fe2a5f1dd0c2a1a97ee3df65")]
    [InlineData("BC1A", false, "e39e86e9ec3c8ab115b0b97e6e82264d67d246b838ca82f3d4f719eecdf9256b")]
    [InlineData("BC1A", true, "ed508b1b00df8bad4df14d84574c239f06167fe225133758cb91668d75093363")]
    [InlineData("BC2", false, "dd82bc96e5608401565235666fc888f4c55e4d79276f2814cc993b4a812d6f45")]
    [InlineData("BC2", true, "2d2acaf3a63d7f912e2d1702d28abb41f04de748ef54a6f01ed849c9a2b57cd6")]
    [InlineData("BC3", false, "fc5f312e9c014f5464743056cbde7666dd24aa1239292609122453a1d1cff496")]
    [InlineData("BC3", true, "ae7c0ce58ce678219e183e616443866e321de9d62ab042450d7f08a5e71bf8c6")]
    [InlineData("BC4U", false, "12106cb00c73b17651a051b0c240bfc5469a0f4422fa7c812356e416aa2050b1")]
    [InlineData("BC4U", true, "23470f1fb4061c43b8b99d30e131983cffa1028b8aa8f13bb3d2ff7277101025")]
    [InlineData("BC4S", false, "e12a92855846650be8c30c22ef6211e94742753a7e4786b3aea20fb0e74f4459")]
    [InlineData("BC4S", true, "3980784e762481777da0e9384b859943a3e983e6a9bcb91159393db92b2ff1cd")]
    public void EncodedBytesAreDeterministic(string format, bool thorough, string expectedSha256)
    {
        // The same bytes on every platform and architecture: a change of the encoder or of its arithmetic changes the hash
        var input = DeterminismBlocks();
        var output = new byte[64 * 16];
        Span<float> samples = stackalloc float[16];
        for (var block = 0; block < 64; block++)
        {
            var texels = input.AsSpan(block * 64, 64);
            var destination = output.AsSpan(block * 16, 16);
            switch (format)
            {
                case "BC1":
                    S3tcBlockEncoder.EncodeBc1(texels, destination, allowTransparency: false, thorough);
                    break;
                case "BC1A":
                    S3tcBlockEncoder.EncodeBc1(texels, destination, allowTransparency: true, thorough);
                    break;
                case "BC2":
                    S3tcBlockEncoder.EncodeBc2(texels, destination, thorough);
                    break;
                case "BC3":
                    S3tcBlockEncoder.EncodeBc3(texels, destination, thorough);
                    break;
                case "BC4U":
                    S3tcBlockEncoder.EncodeUnsignedChannel(texels[1..], 4, destination, thorough);
                    break;
                default:
                    for (var i = 0; i < 16; i++)
                    {
                        samples[i] = (texels[(i * 4) + 2] - 128) / 127.5f;
                    }

                    S3tcBlockEncoder.EncodeSignedChannel(samples, 1, destination, thorough);
                    break;
            }
        }

        Assert.Equal(expectedSha256, Convert.ToHexStringLower(SHA256.HashData(output)));
    }

    private static (double Psnr, int MaxError) Measure(TestImage image, int channels, RoundTrip roundTrip)
    {
        Span<byte> texels = stackalloc byte[64];
        Span<byte> decoded = stackalloc byte[64];
        double squaredError = 0;
        var maximum = 0;
        for (var index = 0; index < image.BlockCount; index++)
        {
            image.CopyBlock(index, texels);
            roundTrip(texels, decoded);
            for (var i = 0; i < 16; i++)
            {
                for (var c = 0; c < channels; c++)
                {
                    var difference = decoded[(i * 4) + c] - texels[(i * 4) + c];
                    squaredError += difference * difference;
                    maximum = Math.Max(maximum, Math.Abs(difference));
                }
            }
        }

        return (Psnr(squaredError, image.BlockCount * 16 * channels), maximum);
    }

    private static double Psnr(double squaredError, int count) => squaredError == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 * count / squaredError);

    private static int SquaredError(ReadOnlySpan<byte> texels, ReadOnlySpan<byte> decoded, int channels)
    {
        // The texels of the blocks of one color are the same: the error of the first texel
        var error = 0;
        for (var c = 0; c < channels; c++)
        {
            error += (decoded[c] - texels[c]) * (decoded[c] - texels[c]);
        }

        for (var i = 1; i < 16; i++)
        {
            Assert.Equal(decoded[..4].ToArray(), decoded.Slice(i * 4, 4).ToArray());
        }

        return error;
    }

    private static HashSet<int> ReachableValues(int maximum, bool threeColorMode)
    {
        var values = new HashSet<int>();
        for (var a = 0; a <= maximum; a++)
        {
            for (var b = 0; b <= maximum; b++)
            {
                values.Add((int)Math.Round(a * 255.0 / maximum, MidpointRounding.AwayFromZero));
                if (threeColorMode)
                {
                    values.Add((int)Math.Round((a + b) * 255.0 / (2 * maximum), MidpointRounding.AwayFromZero));
                }
                else
                {
                    values.Add((int)Math.Round(((2 * a) + b) * 255.0 / (3 * maximum), MidpointRounding.AwayFromZero));
                }
            }
        }

        return values;
    }

    private static int Nearest(HashSet<int> values, int target)
    {
        var best = int.MaxValue;
        foreach (var value in values)
        {
            best = Math.Min(best, (value - target) * (value - target));
        }

        return best;
    }

    private static byte[] Block(Func<int, (int R, int G, int B, int A)> texel)
    {
        var texels = new byte[64];
        for (var i = 0; i < 16; i++)
        {
            var (r, g, b, a) = texel(i);
            texels[i * 4] = (byte)r;
            texels[(i * 4) + 1] = (byte)g;
            texels[(i * 4) + 2] = (byte)b;
            texels[(i * 4) + 3] = (byte)a;
        }

        return texels;
    }
}
