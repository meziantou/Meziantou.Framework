using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The coding passes of Rec. ITU-T T.800, Annex D: the context tables against the tables of the Recommendation, and the
/// block encoder against the block decoder (which the decoder tests compare with files written by OpenJPEG).
/// </summary>
public sealed class Jpeg2000BlockCoderTests
{
    private const uint West = Jpeg2000Tier1Tables.West;
    private const uint East = Jpeg2000Tier1Tables.East;
    private const uint North = Jpeg2000Tier1Tables.North;
    private const uint South = Jpeg2000Tier1Tables.South;
    private const uint NorthWest = Jpeg2000Tier1Tables.NorthWest;
    private const uint NorthEast = Jpeg2000Tier1Tables.NorthEast;
    private const uint SouthWest = Jpeg2000Tier1Tables.SouthWest;
    private const uint SouthEast = Jpeg2000Tier1Tables.SouthEast;

    [Fact]
    public void SignificanceContextsAreTheTableOfTheSpecification()
    {
        // Table D.1, for the LL and LH sub-bands: the horizontal neighbors first, then the vertical ones, then the diagonals
        var low = Jpeg2000Tier1Tables.GetZeroCodingContexts(Jpeg2000Tier1Tables.OrientationLowHorizontal);
        Assert.Equal(0, low[0]);
        Assert.Equal(1, low[(int)NorthWest]);
        Assert.Equal(2, low[(int)(NorthWest | SouthEast)]);
        Assert.Equal(2, low[(int)(NorthWest | NorthEast | SouthWest | SouthEast)]);
        Assert.Equal(3, low[(int)North]);
        Assert.Equal(3, low[(int)(South | NorthWest | NorthEast)]);
        Assert.Equal(4, low[(int)(North | South)]);
        Assert.Equal(5, low[(int)West]);
        Assert.Equal(6, low[(int)(East | SouthWest)]);
        Assert.Equal(7, low[(int)(East | North)]);
        Assert.Equal(7, low[(int)(West | North | South | NorthEast)]);
        Assert.Equal(8, low[(int)(West | East)]);
        Assert.Equal(8, low[0xFF]);

        // The HL sub-band exchanges the horizontal and the vertical neighbors
        var highLow = Jpeg2000Tier1Tables.GetZeroCodingContexts(Jpeg2000Tier1Tables.OrientationHighLow);
        for (var mask = 0u; mask < 256; mask++)
        {
            var exchanged = (mask & 0xF0) | ((mask & (West | East)) << 2) | ((mask & (North | South)) >> 2);
            Assert.Equal(low[(int)exchanged], highLow[(int)mask]);
        }

        // The HH sub-band: the diagonals first, then the sum of the others
        var diagonal = Jpeg2000Tier1Tables.GetZeroCodingContexts(Jpeg2000Tier1Tables.OrientationHighHigh);
        Assert.Equal(0, diagonal[0]);
        Assert.Equal(1, diagonal[(int)West]);
        Assert.Equal(2, diagonal[(int)(West | North)]);
        Assert.Equal(2, diagonal[(int)(West | East | North | South)]);
        Assert.Equal(3, diagonal[(int)NorthWest]);
        Assert.Equal(4, diagonal[(int)(NorthWest | South)]);
        Assert.Equal(5, diagonal[(int)(SouthEast | West | East)]);
        Assert.Equal(6, diagonal[(int)(NorthWest | SouthEast)]);
        Assert.Equal(7, diagonal[(int)(NorthWest | SouthEast | North)]);
        Assert.Equal(8, diagonal[(int)(NorthWest | NorthEast | SouthWest)]);
        Assert.Equal(8, diagonal[0xFF]);
    }

    [Theory]
    [InlineData(0, 0, 9, 0)]
    [InlineData(1, 0, 12, 0)]
    [InlineData(-1, 0, 12, 1)]
    [InlineData(0, 1, 10, 0)]
    [InlineData(0, -1, 10, 1)]
    [InlineData(1, 1, 13, 0)]
    [InlineData(1, -1, 11, 0)]
    [InlineData(-1, 1, 11, 1)]
    [InlineData(-1, -1, 13, 1)]
    public void SignContextsAreTheTableOfTheSpecification(int horizontal, int vertical, int context, int exclusiveOrBit)
    {
        // Tables D.2 and D.3: the contribution of the horizontal neighbors and of the vertical ones, each made of one
        // significant neighbor of that sign here, or of two neighbors that agree
        foreach (var both in new[] { false, true })
        {
            var flags = 0u;
            if (horizontal != 0)
            {
                flags |= West | (horizontal < 0 ? 1u << Jpeg2000Tier1Tables.NegativeShift : 0);
                if (both)
                {
                    flags |= East | (horizontal < 0 ? 1u << (Jpeg2000Tier1Tables.NegativeShift + 1) : 0);
                }
            }

            if (vertical != 0)
            {
                flags |= South | (vertical < 0 ? 1u << (Jpeg2000Tier1Tables.NegativeShift + 3) : 0);
                if (both)
                {
                    flags |= North | (vertical < 0 ? 1u << (Jpeg2000Tier1Tables.NegativeShift + 2) : 0);
                }
            }

            var entry = Jpeg2000Tier1Tables.GetSignContext(flags);
            Assert.Equal((context, exclusiveOrBit), (entry & 0x7F, entry >> 7));
        }

        // Two neighbors of opposite signs contribute nothing
        var opposite = West | East | (1u << Jpeg2000Tier1Tables.NegativeShift);
        Assert.Equal(9, Jpeg2000Tier1Tables.GetSignContext(opposite) & 0x7F);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(4, 4, 8)]
    [InlineData(5, 7, 12)]
    [InlineData(64, 64, 9)]
    [InlineData(3, 64, 16)]
    [InlineData(1024, 4, 5)]
    [InlineData(4, 1024, 5)]
    [InlineData(64, 64, 30)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void EncodedCodeBlocksAreDecodedExactly(int width, int height, int bitPlanes)
    {
        var scope = new AllocationScope(new ImageResourceLimits());
        using var encoder = new Jpeg2000Tier1Encoder(scope);
        using var decoder = new Jpeg2000Tier1Decoder(scope);
        using var output = new PooledPayloadWriter(scope);
        var random = new Random(width + (height * 3) + bitPlanes);
        var samples = new int[width * height];
        var decoded = new int[width * height];
        foreach (var orientation in new[] { Jpeg2000Tier1Tables.OrientationLowHorizontal, Jpeg2000Tier1Tables.OrientationHighLow, Jpeg2000Tier1Tables.OrientationHighHigh })
        {
            // Sparse coefficients, as a sub-band has them, with a few large ones; the top bit-plane is used
            for (var i = 0; i < samples.Length; i++)
            {
                var magnitude = random.Next(4) == 0 ? random.Next(1 << bitPlanes) : random.Next(1 << (bitPlanes / 2));
                samples[i] = random.Next(2) == 0 || magnitude == 0 ? magnitude : magnitude | int.MinValue;
            }

            samples[random.Next(samples.Length)] = (1 << (bitPlanes - 1)) | int.MinValue;
            output.Reset();
            var coded = encoder.Encode(samples, width, height, orientation, output, out var length);
            Assert.Equal(bitPlanes, coded);
            Assert.Equal(output.Length, length);

            // The decoder returns twice the magnitude, plus one when every bit-plane is decoded
            Jpeg2000CodeBlockSegment[] segments = [new(0, length, (3 * bitPlanes) - 2)];
            decoder.Decode(decoded, width, height, orientation, style: 0, bitPlanes, output.WrittenSpan.ToArray(), segments);
            for (var i = 0; i < samples.Length; i++)
            {
                var magnitude = samples[i] & int.MaxValue;
                var expected = magnitude == 0 ? 0 : ((2 * (long)magnitude) + 1) | (samples[i] < 0 ? 0x80000000L : 0);
                Assert.Equal(expected, (uint)decoded[i], message: $"{width}x{height}, orientation {orientation}, coefficient {i}");
            }
        }
    }

    [Fact]
    public void ACodeBlockOfZerosIsNotCoded()
    {
        var scope = new AllocationScope(new ImageResourceLimits());
        using var encoder = new Jpeg2000Tier1Encoder(scope);
        using var output = new PooledPayloadWriter(scope);
        Assert.Equal(0, encoder.Encode(new int[16], 4, 4, Jpeg2000Tier1Tables.OrientationHighHigh, output, out var length));
        Assert.Equal((0, 0), (length, output.Length));
    }

    [Fact]
    public void MissingCodingPassesLeaveTheMiddleOfTheirRange()
    {
        // A coefficient of magnitude 5 in three bit-planes. With its first coding pass only (the cleanup pass of the
        // bit-plane 2), it is known to be between 4 and 7: twice 6, the middle of that range, is returned. Each bit-plane
        // then halves the range: between 4 and 5, then 5
        var scope = new AllocationScope(new ImageResourceLimits());
        using var encoder = new Jpeg2000Tier1Encoder(scope);
        using var decoder = new Jpeg2000Tier1Decoder(scope);
        using var output = new PooledPayloadWriter(scope);
        int[] samples = [5];
        Assert.Equal(3, encoder.Encode(samples, 1, 1, Jpeg2000Tier1Tables.OrientationLowHorizontal, output, out var length));
        var data = output.WrittenSpan.ToArray();
        var decoded = new int[1];
        foreach (var (passes, expected) in new[] { (1, 12), (3, 10), (4, 10), (6, 11), (7, 11) })
        {
            Jpeg2000CodeBlockSegment[] segments = [new(0, length, passes)];
            decoder.Decode(decoded, 1, 1, Jpeg2000Tier1Tables.OrientationLowHorizontal, style: 0, bitPlanes: 3, data, segments);
            Assert.Equal(expected, decoded[0], message: $"{passes} coding passes");
        }

        // More coding passes than three bit-planes have
        Jpeg2000CodeBlockSegment[] tooMany = [new(0, length, 8)];
        Assert.Throws<InvalidImageContentException>(() => decoder.Decode(decoded, 1, 1, Jpeg2000Tier1Tables.OrientationLowHorizontal, style: 0, bitPlanes: 3, data, tooMany));
    }
}
