using System.Security.Cryptography;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The BC6H block encoder: every block decodes with <see cref="BlockDecoder.DecodeBc6H"/> within the error measured for each
/// kind of content and level (in units of the half-float bit patterns, roughly a relative error), solid colors are exact,
/// the values the format cannot store are mapped as documented, every mode is emitted, and the output is the same on every
/// platform.
/// </summary>
public sealed class Bc6HBlockEncoderTests
{
    public enum Content
    {
        Gradient,
        Noise,
        Edges,
        Solid,
        TwoRegions,
        NarrowNoise,
    }

    private const int BlockCount = 96;

    [Theory]
    [InlineData(Content.Gradient, false, false, 122, 950)]
    [InlineData(Content.Noise, false, false, 5250, 22000)]
    [InlineData(Content.Edges, false, false, 8, 16)]
    [InlineData(Content.Solid, false, false, 0, 0)]
    [InlineData(Content.TwoRegions, false, false, 11, 66)]
    [InlineData(Content.NarrowNoise, false, false, 340, 1290)]
    [InlineData(Content.Gradient, false, true, 75, 310)]
    [InlineData(Content.Noise, false, true, 3930, 20700)]
    [InlineData(Content.Edges, false, true, 8, 16)]
    [InlineData(Content.Solid, false, true, 0, 0)]
    [InlineData(Content.TwoRegions, false, true, 8.8, 75)]
    [InlineData(Content.NarrowNoise, false, true, 255, 1150)]
    [InlineData(Content.Gradient, true, false, 121, 1040)]
    [InlineData(Content.Noise, true, false, 10430, 44400)]
    [InlineData(Content.Edges, true, false, 16.8, 33)]
    [InlineData(Content.Solid, true, false, 0, 0)]
    [InlineData(Content.TwoRegions, true, false, 11.3, 72)]
    [InlineData(Content.NarrowNoise, true, false, 9090, 45400)]
    [InlineData(Content.Gradient, true, true, 76, 390)]
    [InlineData(Content.Noise, true, true, 7880, 38600)]
    [InlineData(Content.Edges, true, true, 16.8, 33)]
    [InlineData(Content.Solid, true, true, 0, 0)]
    [InlineData(Content.TwoRegions, true, true, 8.8, 68)]
    [InlineData(Content.NarrowNoise, true, true, 6320, 45400)]
    public void RoundTripStaysWithinTheMeasuredError(Content content, bool isSigned, bool thorough, double maxMean, int maxError)
    {
        // In half-float bit patterns: 1024 is a factor of 2, so a mean of 10 is a relative error of about 0.7%. Noise
        // spans the whole range of the format, which one line (or two) of a block cannot follow
        var (mean, max, _) = Measure(content, isSigned, thorough);
        Assert.InRange(mean, 0, maxMean);
        Assert.InRange(max, 0, maxError);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThoroughIsNeverWorseOnAverage(bool isSigned)
    {
        foreach (var content in Enum.GetValues<Content>())
        {
            Assert.InRange(Measure(content, isSigned, thorough: true).Mean, 0, Measure(content, isSigned, thorough: false).Mean);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SolidColorsAreExact(bool isSigned, bool thorough)
    {
        // Mode 0x0F stores a 16-bit endpoint: the value of the interpolation that decodes to the texel
        var seed = 99u;
        var encoded = new byte[16];
        var decoded = new ushort[64];
        for (var n = 0; n < 300; n++)
        {
            var block = CreateBlock(Content.Solid, isSigned, ref seed);
            Bc6HBlockEncoder.Encode(block, encoded, isSigned, thorough);
            BlockDecoder.DecodeBc6H(encoded, decoded, isSigned);
            for (var i = 0; i < 64; i++)
            {
                var expected = (i & 3) == 3 ? (ushort)0x3C00 : block[i] == 0x8000 ? (ushort)0 : block[i];
                Assert.Equal(expected, decoded[i]);
            }
        }
    }

    [Theory]
    [InlineData(false, 0xBC00, 0)] // -1: the unsigned format has no negative values
    [InlineData(false, 0x8000, 0)] // -0
    [InlineData(false, 0x7E00, 0)] // NaN
    [InlineData(false, 0xFE00, 0)] // NaN
    [InlineData(false, 0x7C00, 0x7BFF)] // +infinity: the largest finite value
    [InlineData(false, 0xFC00, 0)] // -infinity
    [InlineData(false, 0x7BFF, 0x7BFF)]
    [InlineData(false, 0x0001, 0x0001)] // The smallest subnormal
    [InlineData(true, 0xBC00, 0xBC00)]
    [InlineData(true, 0x8000, 0)]
    [InlineData(true, 0x7E00, 0)]
    [InlineData(true, 0xFE01, 0)]
    [InlineData(true, 0x7C00, 0x7BFF)]
    [InlineData(true, 0xFC00, 0xFBFF)]
    [InlineData(true, 0xFBFF, 0xFBFF)]
    [InlineData(true, 0x8001, 0x8001)]
    public void ValuesTheFormatCannotStoreAreMapped(bool isSigned, int half, int expected)
    {
        var block = new ushort[64];
        block.AsSpan().Fill((ushort)half);
        var encoded = new byte[16];
        var decoded = new ushort[64];
        foreach (var thorough in new[] { false, true })
        {
            Bc6HBlockEncoder.Encode(block, encoded, isSigned, thorough);
            BlockDecoder.DecodeBc6H(encoded, decoded, isSigned);
            for (var i = 0; i < 64; i++)
            {
                Assert.Equal((i & 3) == 3 ? 0x3C00 : expected, decoded[i]);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtremeValuesInOneBlockDecodeToFiniteValues(bool isSigned)
    {
        ushort[] values = [0x0000, 0x8000, 0x7C00, 0xFC00, 0x7E00, 0xFFFF, 0x7BFF, 0xFBFF, 0x0001, 0x8001, 0x3C00, 0xBC00];
        var block = new ushort[64];
        for (var i = 0; i < 64; i++)
        {
            block[i] = values[(i * 7) % values.Length];
        }

        var encoded = new byte[16];
        var decoded = new ushort[64];
        foreach (var thorough in new[] { false, true })
        {
            Bc6HBlockEncoder.Encode(block, encoded, isSigned, thorough);
            BlockDecoder.DecodeBc6H(encoded, decoded, isSigned);
            foreach (var value in decoded)
            {
                Assert.InRange(value & 0x7FFF, 0, 0x7BFF);
                Assert.True(isSigned || value < 0x8000);
            }
        }
    }

    [Fact]
    public void WrappedDeltasReachBothEndsOfTheRange()
    {
        // 0 and the largest value: the second 16-bit endpoint of mode 0x0F is a delta of -2 from the first, added modulo 2^16
        var block = new ushort[64];
        for (var i = 0; i < 16; i++)
        {
            block[(i * 4) + 0] = (i & 1) == 0 ? (ushort)0 : (ushort)0x7BFF;
            block[(i * 4) + 1] = (i & 1) == 0 ? (ushort)0 : (ushort)0x7BFF;
            block[(i * 4) + 2] = (i & 1) == 0 ? (ushort)0 : (ushort)0x7BFF;
            block[(i * 4) + 3] = 0x3C00;
        }

        var encoded = new byte[16];
        var decoded = new ushort[64];
        Bc6HBlockEncoder.Encode(block, encoded, isSigned: false, thorough: false);
        BlockDecoder.DecodeBc6H(encoded, decoded, isSigned: false);
        Assert.Equal(0x0F, GetMode(encoded));
        Assert.Equal(block, decoded);
    }

    [Fact]
    public void EveryModeIsEmitted()
    {
        HashSet<int> Modes(bool isSigned, bool thorough)
        {
            var modes = new HashSet<int>();
            foreach (var content in Enum.GetValues<Content>())
            {
                modes.UnionWith(Measure(content, isSigned, thorough).Modes);
            }

            return modes;
        }

        // The fast level only has the modes of one subset; the thorough level all 14 (the signed content of these tests
        // never needs modes 0x16 and 0x1A, which give one more delta bit to green or blue)
        int[] oneSubset = [0x03, 0x07, 0x0B, 0x0F];
        int[] all = [0x00, 0x01, 0x02, 0x03, 0x06, 0x07, 0x0A, 0x0B, 0x0E, 0x0F, 0x12, 0x16, 0x1A, 0x1E];
        Assert.Equal(oneSubset, Modes(isSigned: false, thorough: false).Order().ToArray());
        Assert.Equal(oneSubset, Modes(isSigned: true, thorough: false).Order().ToArray());
        Assert.Equal(all, Modes(isSigned: false, thorough: true).Order().ToArray());
        Assert.Equal(all.Except([0x16, 0x1A]).ToArray(), Modes(isSigned: true, thorough: true).Order().ToArray());
    }

    [Theory]
    [InlineData(false, false, "2df621a377fe59b35e9763f71f15c4380343a88f979219a0392c80ba17f79df4")]
    [InlineData(false, true, "df681952de20fa133ce49a6cbdb35daa5156044b9749f05fc47c369570760a81")]
    [InlineData(true, false, "268d61f3e0318e300e3dedb7b780aeb06ac953568b1ba96a79b375dcc76cfd66")]
    [InlineData(true, true, "4fa5eddcd001e35cde87211d01eace98518bacf0e6fa2bfc46549b8787b9273f")]
    public void OutputIsTheSameOnEveryPlatform(bool isSigned, bool thorough, string expected)
    {
        Assert.Equal(expected, ComputeHash(isSigned, thorough));
    }

    /// <summary>A small linear congruential generator, so that the content is the same on every platform.</summary>
    private static uint Next(ref uint seed)
    {
        seed = (seed * 1664525) + 1013904223;
        return seed >> 8;
    }

    /// <summary>A random finite half-float bit pattern, negative half of the time when <paramref name="isSigned"/>.</summary>
    private static int RandomHalf(ref uint seed, bool isSigned, int min, int max)
    {
        var magnitude = min + (int)(Next(ref seed) % (uint)(max - min + 1));
        return isSigned && (Next(ref seed) & 1) != 0 ? magnitude | 0x8000 : magnitude;
    }

    /// <summary>
    /// Creates a block of 16 RGBA texels of half-float bit patterns, row by row. The gradients are linear in the bit
    /// patterns: exponential in value, like the light of HDR content.
    /// </summary>
    internal static ushort[] CreateBlock(Content content, bool isSigned, ref uint seed)
    {
        var block = new ushort[64];
        for (var i = 0; i < 16; i++)
        {
            block[(i * 4) + 3] = 0x3C00;
        }

        switch (content)
        {
            case Content.Gradient:
                for (var channel = 0; channel < 3; channel++)
                {
                    var origin = RandomHalf(ref seed, isSigned, 0x1000, 0x6000);
                    var slopeX = (int)(Next(ref seed) % 801) - 400;
                    var slopeY = (int)(Next(ref seed) % 801) - 400;
                    for (var i = 0; i < 16; i++)
                    {
                        var magnitude = Math.Clamp((origin & 0x7FFF) + (slopeX * (i & 3)) + (slopeY * (i >> 2)), 0, 0x7BFF);
                        block[(i * 4) + channel] = (ushort)((origin & 0x8000) | magnitude);
                    }
                }

                break;

            case Content.Noise:
                for (var i = 0; i < 16; i++)
                {
                    for (var channel = 0; channel < 3; channel++)
                    {
                        block[(i * 4) + channel] = (ushort)RandomHalf(ref seed, isSigned, 0, 0x7BFF);
                    }
                }

                break;

            case Content.Edges:
                {
                    Span<int> color1 = [RandomHalf(ref seed, isSigned, 0, 0x7BFF), RandomHalf(ref seed, isSigned, 0, 0x7BFF), RandomHalf(ref seed, isSigned, 0, 0x7BFF)];
                    Span<int> color2 = [RandomHalf(ref seed, isSigned, 0, 0x7BFF), RandomHalf(ref seed, isSigned, 0, 0x7BFF), RandomHalf(ref seed, isSigned, 0, 0x7BFF)];
                    var vertical = (Next(ref seed) & 1) != 0;
                    var position = (int)(Next(ref seed) % 3) + 1;
                    for (var i = 0; i < 16; i++)
                    {
                        var color = (vertical ? i & 3 : i >> 2) < position ? color1 : color2;
                        for (var channel = 0; channel < 3; channel++)
                        {
                            block[(i * 4) + channel] = (ushort)color[channel];
                        }
                    }

                    break;
                }

            case Content.TwoRegions:
                {
                    // The two subsets of a partition, each a gradient, apart by an offset of a random magnitude per channel
                    var partition = (int)(Next(ref seed) % 32);
                    var magnitudes = Next(ref seed);
                    for (var channel = 0; channel < 3; channel++)
                    {
                        var origin = RandomHalf(ref seed, isSigned, 0x2000, 0x6000);
                        var spread = 1 << (int)((magnitudes >> (channel * 4)) % 13);
                        var offset = (int)(Next(ref seed) % (uint)((2 * spread) + 1)) - spread;
                        var slope = (int)(Next(ref seed) % 64) - 32;
                        for (var i = 0; i < 16; i++)
                        {
                            var subset = BptcTables.GetSubset(2, partition, i);
                            var magnitude = Math.Clamp((origin & 0x7FFF) + (subset * offset) + (slope * (i & 3)), 0, 0x7BFF);
                            block[(i * 4) + channel] = (ushort)((origin & 0x8000) | magnitude);
                        }
                    }

                    break;
                }

            case Content.NarrowNoise:
                {
                    // Noise within a range of 2048 bit patterns: about one octave
                    var origin = (int)(Next(ref seed) % 0x5800) + 0x1000;
                    for (var i = 0; i < 16; i++)
                    {
                        for (var channel = 0; channel < 3; channel++)
                        {
                            block[(i * 4) + channel] = (ushort)RandomHalf(ref seed, isSigned, origin, origin + 0x7FF);
                        }
                    }

                    break;
                }

            default:
                {
                    Span<int> color = [RandomHalf(ref seed, isSigned, 0, 0x7BFF), RandomHalf(ref seed, isSigned, 0, 0x7BFF), RandomHalf(ref seed, isSigned, 0, 0x7BFF)];
                    for (var i = 0; i < 16; i++)
                    {
                        for (var channel = 0; channel < 3; channel++)
                        {
                            block[(i * 4) + channel] = (ushort)color[channel];
                        }
                    }

                    break;
                }
        }

        return block;
    }

    /// <summary>Gets the mode number of a BC6H block (Table 121): 2 bits, or 5 when the first two are 10 or 11.</summary>
    internal static int GetMode(ReadOnlySpan<byte> block) => (block[0] & 3) < 2 ? block[0] & 3 : block[0] & 0x1F;

    /// <summary>The difference of two half-float bit patterns as sign-magnitude integers (-0 is 0).</summary>
    private static int HalfDistance(ushort expected, ushort actual)
    {
        static int ToInteger(ushort half) => (half & 0x8000) != 0 ? -(half & 0x7FFF) : half;
        return Math.Abs(ToInteger(expected) - ToInteger(actual));
    }

    /// <summary>Encodes and decodes blocks of a content; returns the mean and the largest error in half-float bit patterns.</summary>
    internal static (double Mean, int Max, HashSet<int> Modes) Measure(Content content, bool isSigned, bool thorough)
    {
        var seed = (uint)content + (isSigned ? 100u : 1u);
        var encoded = new byte[16];
        var decoded = new ushort[64];
        var modes = new HashSet<int>();
        long sum = 0;
        var max = 0;
        for (var n = 0; n < BlockCount; n++)
        {
            var block = CreateBlock(content, isSigned, ref seed);
            Bc6HBlockEncoder.Encode(block, encoded, isSigned, thorough);
            BlockDecoder.DecodeBc6H(encoded, decoded, isSigned);
            modes.Add(GetMode(encoded));
            for (var i = 0; i < 64; i++)
            {
                if ((i & 3) == 3)
                    continue;

                var error = HalfDistance(block[i], decoded[i]);
                sum += error;
                max = Math.Max(max, error);
            }
        }

        return ((double)sum / (BlockCount * 48), max, modes);
    }

    /// <summary>Encodes blocks of every content; returns the SHA-256 of the blocks.</summary>
    internal static string ComputeHash(bool isSigned, bool thorough)
    {
        var seed = 2024u;
        var output = new List<byte>();
        var encoded = new byte[16];
        for (var n = 0; n < 64; n++)
        {
            Bc6HBlockEncoder.Encode(CreateBlock((Content)(n % 4), isSigned, ref seed), encoded, isSigned, thorough);
            output.AddRange(encoded);
        }

        return Convert.ToHexStringLower(SHA256.HashData(output.ToArray()));
    }
}
