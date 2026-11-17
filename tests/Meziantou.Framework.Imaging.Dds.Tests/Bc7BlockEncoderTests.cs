using System.Numerics;
using System.Security.Cryptography;
using Meziantou.Framework.Imaging.Internals;
using static Meziantou.Framework.Imaging.Tests.BlockEncoderTestImages;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The BC7 block encoder, through <see cref="BlockDecoder.DecodeBc7"/>: the error bounds measured on each kind of content at
/// both levels, the modes, rotations and index selections chosen for crafted blocks, the alpha of opaque and constant-alpha
/// blocks, blocks of one color, extreme inputs and the determinism of the encoded bytes.
/// </summary>
public sealed class Bc7BlockEncoderTests
{
    [Theory]
    [InlineData("photo", false, 39.1, 16)]
    [InlineData("photo", true, 41.0, 12)]
    [InlineData("photo+alpha", false, 35.2, 26)]
    [InlineData("photo+alpha", true, 35.9, 26)]
    [InlineData("gradient", false, 46.5, 2)]
    [InlineData("gradient", true, 46.5, 4)]
    [InlineData("noise", false, 17.8, 185)]
    [InlineData("noise", true, 19.3, 140)]
    [InlineData("noise+alpha", false, 15.3, 180)]
    [InlineData("noise+alpha", true, 15.5, 180)]
    [InlineData("edges", false, 52.2, 1)]
    [InlineData("edges", true, 57.0, 1)]
    [InlineData("solid", false, double.PositiveInfinity, 0)]
    [InlineData("solid+alpha", true, double.PositiveInfinity, 0)]
    [InlineData("alpha-gradient", false, 41.9, 5)]
    [InlineData("alpha-gradient", true, 45.8, 4)]
    [InlineData("cutout", false, 41.9, 7)]
    [InlineData("cutout", true, 51.3, 9)]
    public void RoundTripStaysWithinBounds(string name, bool thorough, double minimumPsnr, int maximumError)
    {
        var image = Get(name);
        Span<byte> texels = stackalloc byte[64];
        Span<byte> decoded = stackalloc byte[64];
        Span<byte> block = stackalloc byte[16];
        double squaredError = 0;
        var maximum = 0;
        for (var index = 0; index < image.BlockCount; index++)
        {
            image.CopyBlock(index, texels);
            Bc7BlockEncoder.Encode(texels, block, thorough);
            BlockDecoder.DecodeBc7(block, decoded);
            var constantAlpha = IsAlphaConstant(texels);
            for (var i = 0; i < 64; i++)
            {
                var difference = decoded[i] - texels[i];
                squaredError += difference * difference;
                maximum = Math.Max(maximum, Math.Abs(difference));
                if (constantAlpha && i % 4 == 3)
                {
                    Assert.Equal(texels[i], decoded[i]);
                }
            }
        }

        var psnr = squaredError == 0 ? double.PositiveInfinity : 10 * Math.Log10(255.0 * 255.0 * image.BlockCount * 64 / squaredError);
        Assert.InRange(psnr, minimumPsnr, double.PositiveInfinity, message: name);
        Assert.InRange(maximum, 0, maximumError, message: name);
    }

    [Theory]
    [InlineData("gray ramp", false, 6, -1, -1, 3)]
    [InlineData("solid", false, 5, 0, -1, 0)]
    [InlineData("solid", true, 5, 0, -1, 0)]
    [InlineData("two colors and an alpha ramp", false, 4, 0, 0, 10)]
    [InlineData("color ramp and two alphas", true, 4, 0, 1, 10)]
    [InlineData("red apart", true, 4, 1, 1, 27)]
    [InlineData("green apart", true, 4, 2, 1, 27)]
    [InlineData("blue apart", true, 4, 3, 1, 27)]
    [InlineData("red gradient apart", true, 5, 1, -1, 1)]
    [InlineData("green gradient apart", true, 5, 2, -1, 1)]
    [InlineData("blue gradient apart", true, 5, 3, -1, 1)]
    [InlineData("alpha gradient apart", false, 5, 0, -1, 1)]
    [InlineData("two ramps", false, 1, -1, -1, 13)]
    [InlineData("two ramps", true, 2, -1, -1, 10)]
    [InlineData("left and right", false, 3, -1, -1, 0)]
    [InlineData("three columns", true, 0, -1, -1, 4)]
    [InlineData("two ramps with alpha", true, 7, -1, -1, 12)]
    public void CraftedBlocksUseTheExpectedMode(string name, bool thorough, int mode, int rotation, int indexSelection, int maximumError)
    {
        var texels = Crafted(name);
        var block = new byte[16];
        Bc7BlockEncoder.Encode(texels, block, thorough);
        Assert.Equal(mode, BitOperations.TrailingZeroCount(block[0] | 0x100));
        if (rotation >= 0)
        {
            Assert.Equal(rotation, (block[0] >> (mode + 1)) & 3);
        }

        if (indexSelection >= 0)
        {
            Assert.Equal(indexSelection, (block[0] >> 7) & 1);
        }

        var decoded = new byte[64];
        BlockDecoder.DecodeBc7(block, decoded);
        var error = 0;
        for (var i = 0; i < 64; i++)
        {
            error = Math.Max(error, Math.Abs(decoded[i] - texels[i]));
        }

        Assert.Equal(maximumError, error);
    }

    [Fact]
    public void ThoroughLevelUsesEveryModeRotationAndIndexSelection()
    {
        // Mode 4 and 5 configurations as "mode, rotation, index selection"; the other modes alone
        var used = new HashSet<string>(StringComparer.Ordinal);
        var block = new byte[16];
        Span<byte> texels = stackalloc byte[64];
        foreach (var name in new[] { "photo", "photo+alpha", "gradient", "noise", "noise+alpha", "edges", "alpha-gradient" })
        {
            var image = Get(name);
            for (var index = 0; index < image.BlockCount; index++)
            {
                image.CopyBlock(index, texels);
                Bc7BlockEncoder.Encode(texels, block, thorough: true);
                var mode = BitOperations.TrailingZeroCount(block[0] | 0x100);
                used.Add(mode switch
                {
                    4 => $"4 {(block[0] >> 5) & 3} {block[0] >> 7}",
                    5 => $"5 {(block[0] >> 6) & 3}",
                    _ => mode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                });
            }
        }

        string[] expected = ["0", "1", "2", "3", "4 0 0", "4 0 1", "4 1 0", "4 1 1", "4 2 0", "4 2 1", "4 3 0", "4 3 1", "5 0", "5 1", "5 2", "5 3", "6", "7"];
        Assert.Equal(expected, used.Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConstantAlphaIsKeptExactly(bool thorough)
    {
        var random = new Lcg(11);
        var texels = new byte[64];
        var block = new byte[16];
        var decoded = new byte[64];
        foreach (var alpha in new[] { 255, 0, 1, 77, 128, 254 })
        {
            for (var sample = 0; sample < 32; sample++)
            {
                // Noise, then smooth blocks
                for (var i = 0; i < 16; i++)
                {
                    texels[i * 4] = sample % 2 == 0 ? random.Next() : (byte)(sample + (i * 8));
                    texels[(i * 4) + 1] = sample % 2 == 0 ? random.Next() : (byte)(200 - (i * 4));
                    texels[(i * 4) + 2] = sample % 2 == 0 ? random.Next() : (byte)(sample * 7);
                    texels[(i * 4) + 3] = (byte)alpha;
                }

                Bc7BlockEncoder.Encode(texels, block, thorough);
                BlockDecoder.DecodeBc7(block, decoded);
                for (var i = 0; i < 16; i++)
                {
                    Assert.Equal(alpha, decoded[(i * 4) + 3]);
                }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SolidColorsAreExact(bool thorough)
    {
        var random = new Lcg(5);
        var texels = new byte[64];
        var block = new byte[16];
        var decoded = new byte[64];
        for (var sample = 0; sample < 512; sample++)
        {
            int r, g, b, a;
            if (sample < 256)
            {
                // Every value in every channel
                (r, g, b, a) = (sample, 255 - sample, (sample * 7) & 0xFF, sample);
            }
            else
            {
                (r, g, b, a) = (random.Next(), random.Next(), random.Next(), random.Next());
            }

            for (var i = 0; i < 16; i++)
            {
                texels[i * 4] = (byte)r;
                texels[(i * 4) + 1] = (byte)g;
                texels[(i * 4) + 2] = (byte)b;
                texels[(i * 4) + 3] = (byte)a;
            }

            Bc7BlockEncoder.Encode(texels, block, thorough);
            BlockDecoder.DecodeBc7(block, decoded);
            Assert.Equal(texels, decoded);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(255, 255)]
    [InlineData(0, 255)]
    [InlineData(255, 0)]
    public void ExtremeValuesAreExact(int color, int alpha)
    {
        var texels = new byte[64];
        for (var i = 0; i < 16; i++)
        {
            texels[i * 4] = (byte)color;
            texels[(i * 4) + 1] = (byte)color;
            texels[(i * 4) + 2] = (byte)color;
            texels[(i * 4) + 3] = (byte)alpha;
        }

        foreach (var thorough in new[] { false, true })
        {
            var block = new byte[16];
            Bc7BlockEncoder.Encode(texels, block, thorough);
            var decoded = new byte[64];
            BlockDecoder.DecodeBc7(block, decoded);
            Assert.Equal(texels, decoded);
        }
    }

    [Theory]
    [InlineData(false, "5b1a427114761477a7514a617c42ac2db33e99d1a74350fd4dbe7258e039b754")]
    [InlineData(true, "f110aed815c27251432e2bf80852ee82aa854f2cbdd5c7be198ea32812f58bba")]
    public void EncodedBytesAreDeterministic(bool thorough, string expectedSha256)
    {
        // The same bytes on every platform and architecture: a change of the encoder or of its arithmetic changes the hash
        var input = DeterminismBlocks();
        var output = new byte[64 * 16];
        for (var block = 0; block < 64; block++)
        {
            Bc7BlockEncoder.Encode(input.AsSpan(block * 64, 64), output.AsSpan(block * 16, 16), thorough);
        }

        Assert.Equal(expectedSha256, Convert.ToHexStringLower(SHA256.HashData(output)));
    }

    private static bool IsAlphaConstant(ReadOnlySpan<byte> texels)
    {
        for (var i = 1; i < 16; i++)
        {
            if (texels[(i * 4) + 3] != texels[3])
                return false;
        }

        return true;
    }

    private static byte[] Crafted(string name)
    {
        Func<int, (int R, int G, int B, int A)> texel = name switch
        {
            "gray ramp" => i => (i * 17, i * 17, i * 17, 255),
            "solid" => i => (12, 34, 56, 78),
            "two colors and an alpha ramp" => i => (i % 2 == 0 ? 0 : 255, 0, 0, i * 17),
            "color ramp and two alphas" => i => (i * 17, i * 17, 0, (i / 4) % 2 == 0 ? 0 : 255),
            "red apart" => i => ((i * 7) % 16 * 17, i * 17, i * 17, 255),
            "green apart" => i => (i * 17, (i * 7) % 16 * 17, i * 17, 255),
            "blue apart" => i => (i * 17, i * 17, (i * 7) % 16 * 17, 255),
            "red gradient apart" => i => (100 + ((i / 4) * 5), 50, 128 + ((i % 4) * 3), 255),
            "green gradient apart" => i => (100, 50 + ((i % 4) * 3), 128 + ((i / 4) * 5), 255),
            "blue gradient apart" => i => (100 + ((i / 4) * 5), 50 + ((i / 4) * 5), 128 + ((i % 4) * 3), 255),
            "alpha gradient apart" => i => (100 + ((i % 4) * 3), 50, 128, 30 + ((i / 4) * 5)),
            "two ramps" => i => (i % 4) < 2 ? (i * 16, 0, 0, 255) : (0, 0, i * 16, 255),
            "left and right" => i => (i % 4) < 2 ? (200 + ((i / 4) * 10), 30, 30, 255) : (30, 30, 200 + ((i / 4) * 10), 255),
            "three columns" => i => (i % 4) switch
            {
                0 => (250 - (i * 4), 0, 0, 255),
                1 => (0, 250 - (i * 4), 0, 255),
                _ => (0, 0, 250 - (i * 4), 255),
            },
            "two ramps with alpha" => i => (i % 4) < 2 ? (i * 16, 0, 0, i * 16) : (0, 0, i * 16, 255 - (i * 16)),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, message: null),
        };

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
