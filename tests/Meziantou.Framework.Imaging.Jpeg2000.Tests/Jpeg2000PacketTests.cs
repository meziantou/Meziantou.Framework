using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The pieces of the packets of Rec. ITU-T T.800, Annex B, on bits written by hand: the bit stuffing of the packet
/// headers, the tag trees, the partition of a tile into resolution levels, sub-bands, precincts and code-blocks, and the
/// termination of the codeword segments.
/// </summary>
public sealed class Jpeg2000PacketTests
{
    [Fact]
    public void ABitIsStuffedAfterAnFfByte()
    {
        // B.10.1: the byte that follows an FF byte holds seven bits, after a 0 bit
        ReadOnlySpan<byte> data = [0xFF, 0x7F, 0x80, 0xFF, 0x00, 0x55];
        var reader = new Jpeg2000PacketBitReader(data, position: 0);
        Assert.Equal(0xFF, reader.ReadBits(8));
        Assert.Equal(0x7F, reader.ReadBits(7));
        Assert.Equal(1, reader.ReadBit());
        Assert.Equal(0, reader.ReadBits(7));
        Assert.Equal(0xFF, reader.ReadBits(8));

        // The header ends after an FF byte: the stuffed byte is skipped
        Assert.Equal(5, reader.Align());
        Assert.Equal(0x55, reader.ReadBits(8));
        Assert.Equal(6, reader.Align());
        var end = new Jpeg2000PacketBitReader(data, position: 6);
        var exception = default(InvalidImageContentException);
        try
        {
            end.ReadBit();
        }
        catch (InvalidImageContentException ex)
        {
            exception = ex;
        }

        Assert.NotNull(exception);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void WrittenBitsAreReadBack(int seed)
    {
        // Runs of 1 bits produce FF bytes, at the end of the header too
        var random = new Random(seed);
        var bits = new int[2000 + seed];
        for (var i = 0; i < bits.Length; i++)
        {
            bits[i] = random.Next(10) < 8 ? 1 : 0;
        }

        bits.AsSpan(bits.Length - 20).Fill(1);
        var scope = new AllocationScope(new ImageResourceLimits());
        using var output = new PooledPayloadWriter(scope);
        var writer = new Jpeg2000PacketBitWriter(output);
        foreach (var bit in bits)
        {
            writer.WriteBit(bit);
        }

        writer.Flush();
        var data = output.WrittenSpan;
        Assert.NotEqual(0xFF, data[^1]);
        for (var i = 0; i + 1 < data.Length; i++)
        {
            Assert.False(data[i] == 0xFF && data[i + 1] > 0x7F, $"marker at {i}");
        }

        var reader = new Jpeg2000PacketBitReader(data, position: 0);
        for (var i = 0; i < bits.Length; i++)
        {
            Assert.Equal(bits[i], reader.ReadBit(), message: $"bit {i}");
        }

        Assert.Equal(data.Length, reader.Align());
    }

    [Fact]
    public void ATagTreeIsDecodedFromHandWrittenBits()
    {
        // B.10.2: a tree over 2x2 leaves whose values are 1 and 3, then 2 and 2: its root is their minimum, 1. Reading the
        // first leaf up to its value gives the root (a 0 bit raises its bound to 1, a 1 bit says it is its value) and the
        // leaf (a 1 bit: its value is the bound of the root). The second leaf starts from the bound 1: two 0 bits and a 1
        // bit. The last two leaves: one 0 bit and a 1 bit each
        ReadOnlySpan<byte> data = [0b0110_0101, 0b0100_0000];
        var reader = new Jpeg2000PacketBitReader(data, position: 0);
        var nodes = new int[(int)(2 * Jpeg2000TagTree.GetNodeCount(2, 2))];
        Assert.HasCount(10, nodes);
        Jpeg2000TagTree.Reset(nodes);

        // A threshold that is not above the value reads what tells so, and nothing more
        Assert.False(Jpeg2000TagTree.IsLessThan(nodes, 2, 2, 0, 0, threshold: 1, ref reader));
        Assert.True(Jpeg2000TagTree.IsLessThan(nodes, 2, 2, 0, 0, threshold: 2, ref reader));
        Assert.False(Jpeg2000TagTree.IsLessThan(nodes, 2, 2, 1, 0, threshold: 3, ref reader));
        Assert.True(Jpeg2000TagTree.IsLessThan(nodes, 2, 2, 1, 0, threshold: 4, ref reader));
        Assert.True(Jpeg2000TagTree.IsLessThan(nodes, 2, 2, 0, 1, threshold: 3, ref reader));
        Assert.True(Jpeg2000TagTree.IsLessThan(nodes, 2, 2, 1, 1, threshold: 3, ref reader));

        // What is known is not read again
        Assert.True(Jpeg2000TagTree.IsLessThan(nodes, 2, 2, 1, 0, threshold: 10, ref reader));
        Assert.False(Jpeg2000TagTree.IsLessThan(nodes, 2, 2, 1, 0, threshold: 3, ref reader));
        Assert.Equal(2, reader.Align());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 7)]
    [InlineData(5, 3)]
    [InlineData(16, 16)]
    [InlineData(33, 2)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void WrittenTagTreesAreReadBack(int width, int height)
    {
        var random = new Random(width + (31 * height));
        var values = new int[width * height];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = random.Next(0, 9);
        }

        var scope = new AllocationScope(new ImageResourceLimits());
        using var output = new PooledPayloadWriter(scope);
        using var encoder = new Jpeg2000TagTreeEncoder(scope);
        values.CopyTo(encoder.Begin(width, height));
        encoder.Complete();
        var writer = new Jpeg2000PacketBitWriter(output);

        // A first threshold for every leaf, as the inclusion in a layer; then the value of every leaf
        for (var i = 0; i < values.Length; i++)
        {
            encoder.Encode(ref writer, i % width, i / width, threshold: 3);
        }

        for (var i = 0; i < values.Length; i++)
        {
            encoder.Encode(ref writer, i % width, i / width, values[i] + 1);
        }

        writer.Flush();
        var reader = new Jpeg2000PacketBitReader(output.WrittenSpan, position: 0);
        var nodes = new int[(int)(2 * Jpeg2000TagTree.GetNodeCount(width, height))];
        Jpeg2000TagTree.Reset(nodes);
        for (var i = 0; i < values.Length; i++)
        {
            Assert.Equal(values[i] < 3, Jpeg2000TagTree.IsLessThan(nodes, width, height, i % width, i / width, threshold: 3, ref reader), message: $"leaf {i}");
        }

        for (var i = 0; i < values.Length; i++)
        {
            var threshold = 1;
            while (!Jpeg2000TagTree.IsLessThan(nodes, width, height, i % width, i / width, threshold, ref reader))
            {
                threshold++;
            }

            Assert.Equal(values[i], threshold - 1, message: $"leaf {i}");
        }

        Assert.Equal(output.Length, reader.Align());
    }

    [Fact]
    public void CodewordSegmentsEndWhereTheCoderIsTerminated()
    {
        // Table D.9. Without style, the coder is never terminated between two coding passes
        for (var pass = 0; pass < 40; pass++)
        {
            Assert.False(Jpeg2000CodeBlockSegment.IsTerminatedAfter(pass, style: 0));
            Assert.True(Jpeg2000CodeBlockSegment.IsTerminatedAfter(pass, Jpeg2000CodingStyle.StyleTerminateAll));
            Assert.True(Jpeg2000CodeBlockSegment.IsTerminatedAfter(pass, Jpeg2000CodingStyle.StyleTerminateAll | Jpeg2000CodingStyle.StyleBypass));
        }

        // With the bypass: after the cleanup pass of the fourth bit-plane (the tenth pass), then after each magnitude
        // refinement pass and each cleanup pass, and never after a significance propagation pass
        var terminated = Enumerable.Range(0, 19).Where(pass => Jpeg2000CodeBlockSegment.IsTerminatedAfter(pass, Jpeg2000CodingStyle.StyleBypass)).ToArray();
        Assert.Equal([9, 11, 12, 14, 15, 17, 18], terminated);
    }

    [Fact]
    public void ATileIsPartitionedAsTheEquationsOfAnnexBSay()
    {
        // An image of 13x11 at the offset (3, 2) of the reference grid, in one tile, with two decomposition levels,
        // code-blocks of 4x4 and precincts of 8x8 at the highest resolution level and 4x4 below
        var geometry = CreateGeometry(xSize: 16, ySize: 13, xOffset: 3, yOffset: 2, levels: 2, blockExponent: 2, precincts: [0x22, 0x22, 0x33]);
        var resolutions = geometry.Resolutions;

        // B-14: the corners of a resolution level are those of the tile-component divided by 2^(levels - r), rounded up
        Assert.Equal((1L, 1L, 4L, 4L), (resolutions[0].X0, resolutions[0].Y0, resolutions[0].X1, resolutions[0].Y1));
        Assert.Equal((2L, 1L, 8L, 7L), (resolutions[1].X0, resolutions[1].Y0, resolutions[1].X1, resolutions[1].Y1));
        Assert.Equal((3L, 2L, 16L, 13L), (resolutions[2].X0, resolutions[2].Y0, resolutions[2].X1, resolutions[2].Y1));

        // B-16: the precincts are anchored at the origin: 4x4 precincts over [1, 4) are one precinct, over [2, 8) x [1, 7)
        // two by two, and 8x8 precincts over [3, 16) x [2, 13) two by two
        Assert.Equal((1, 1), (resolutions[0].PrecinctsWide, resolutions[0].PrecinctsHigh));
        Assert.Equal((2, 2), (resolutions[1].PrecinctsWide, resolutions[1].PrecinctsHigh));
        Assert.Equal((2, 2), (resolutions[2].PrecinctsWide, resolutions[2].PrecinctsHigh));

        // B-15: the sub-bands. The LL sub-band of the level 2, then HL, LH and HH of the level 2 and of the level 1: a
        // high-pass direction is offset by half of the step of its level before the division
        var bands = geometry.Bands;
        Assert.HasCount(7, bands);
        Assert.Equal((1L, 1L, 4L, 4L), Bounds(bands[0]));
        Assert.Equal((1L, 1L, 4L, 4L), Bounds(bands[1]));
        Assert.Equal((1L, 0L, 4L, 3L), Bounds(bands[2]));
        Assert.Equal((1L, 0L, 4L, 3L), Bounds(bands[3]));
        Assert.Equal((1L, 1L, 8L, 7L), Bounds(bands[4]));
        Assert.Equal((2L, 1L, 8L, 6L), Bounds(bands[5]));
        Assert.Equal((1L, 1L, 8L, 6L), Bounds(bands[6]));

        // The sub-bands of a level surround the lower resolution level in the plane, whose size is 3x3 then 6x6
        Assert.Equal([(0, 0), (3, 0), (0, 3), (3, 3), (6, 0), (0, 6), (6, 6)], bands.Select(band => (band.PlaneX, band.PlaneY)).ToArray());

        // B-17 and B-18: a code-block is at most as large as the precinct of its sub-band: 4x4 at the lowest resolution
        // level, 2x2 in the sub-bands of the level 2 (precincts of 4x4 that cover 2x2 coefficients of each sub-band), 4x4
        // in those of the level 1
        Assert.Equal([2, 1, 1, 1, 2, 2, 2], bands.Select(band => band.BlockWidthExponent).ToArray());
        Assert.Equal([(1, 1), (2, 2), (2, 2), (2, 2), (2, 2), (2, 2), (2, 2)], bands.Select(band => (band.BlocksWide, band.BlocksHigh)).ToArray());

        // The code-blocks of the HL sub-band of the level 1, [1, 8) x [1, 7): the partition cuts it at 4 in both directions
        var blocks = geometry.Blocks.Slice(bands[4].FirstBlock, 4);
        Assert.Equal((0, 0, 3, 3), (blocks[0].X, blocks[0].Y, blocks[0].Width, blocks[0].Height));
        Assert.Equal((3, 0, 4, 3), (blocks[1].X, blocks[1].Y, blocks[1].Width, blocks[1].Height));
        Assert.Equal((0, 3, 3, 3), (blocks[2].X, blocks[2].Y, blocks[2].Width, blocks[2].Height));
        Assert.Equal((3, 3, 4, 3), (blocks[3].X, blocks[3].Y, blocks[3].Width, blocks[3].Height));

        // Each of the four precincts of the highest resolution level, 8x8 at [0, 8) and [8, 16), holds one code-block of
        // each of its sub-bands
        Assert.Equal(4, resolutions[2].PrecinctCount);
        var precinctBands = geometry.PrecinctBands.Slice(resolutions[2].FirstPrecinctBand, 12);
        for (var i = 0; i < 12; i++)
        {
            Assert.Equal((1, 1), (precinctBands[i].BlocksWide, precinctBands[i].BlocksHigh));
            Assert.Equal(((i / 3) % 2, (i / 3) / 2), (precinctBands[i].BlockX, precinctBands[i].BlockY));
        }

        geometry.Dispose();

        static (long X0, long Y0, long X1, long Y1) Bounds(Jpeg2000BandGeometry band) => (band.X0, band.Y0, band.X1, band.Y1);
    }

    [Fact]
    public void ASubsampledComponentHasItsOwnPartition()
    {
        // B-12: the corners of a tile-component are those of the tile divided by the sub-sampling factors, rounded up
        var components = new[] { new Jpeg2000ComponentSize(8, IsSigned: false, 1, 1), new Jpeg2000ComponentSize(8, IsSigned: false, 2, 3) };
        var siz = new Jpeg2000Siz(0, 17, 11, 3, 2, 8, 8, 1, 0, components);
        Assert.Equal((2, 2, 4), (siz.TilesAcross, siz.TilesDown, siz.TileCount));
        var headers = CreateHeaders(componentCount: 2, levels: 1, blockExponent: 6, precincts: null);
        var coding = Jpeg2000TileCoding.Resolve(siz, headers, tile: null, capabilities: null);
        var scope = new AllocationScope(new ImageResourceLimits());

        // The tile 3 is [9, 17) x [8, 11) on the reference grid
        using var geometry = Jpeg2000TileGeometry.Create(scope, siz, coding, tileIndex: 3);
        Assert.Equal((9L, 8L, 17L, 11L), (geometry.X0, geometry.Y0, geometry.X1, geometry.Y1));
        Assert.Equal((9L, 8L, 17L, 11L), (geometry.Components[0].X0, geometry.Components[0].Y0, geometry.Components[0].X1, geometry.Components[0].Y1));
        Assert.Equal((5L, 3L, 9L, 4L), (geometry.Components[1].X0, geometry.Components[1].Y0, geometry.Components[1].X1, geometry.Components[1].Y1));

        // The first tile starts at the image area, not at the tile grid
        using var first = Jpeg2000TileGeometry.Create(scope, siz, coding, tileIndex: 0);
        Assert.Equal((3L, 2L, 9L, 8L), (first.X0, first.Y0, first.X1, first.Y1));
        Assert.Equal((2L, 1L, 5L, 3L), (first.Components[1].X0, first.Components[1].Y0, first.Components[1].X1, first.Components[1].Y1));
    }

    private static Jpeg2000TileGeometry CreateGeometry(int xSize, int ySize, int xOffset, int yOffset, int levels, int blockExponent, byte[]? precincts)
    {
        var siz = new Jpeg2000Siz(0, xSize, ySize, xOffset, yOffset, xSize, ySize, 0, 0, [new Jpeg2000ComponentSize(8, IsSigned: false, 1, 1)]);
        var coding = Jpeg2000TileCoding.Resolve(siz, CreateHeaders(1, levels, blockExponent, precincts), tile: null, capabilities: null);
        return Jpeg2000TileGeometry.Create(new AllocationScope(new ImageResourceLimits()), siz, coding, tileIndex: 0);
    }

    private static Jpeg2000HeaderSet CreateHeaders(int componentCount, int levels, int blockExponent, byte[]? precincts)
    {
        var bandCount = (3 * levels) + 1;
        var exponents = new byte[bandCount];
        exponents.AsSpan().Fill(10);
        return new Jpeg2000HeaderSet(componentCount)
        {
            CodingDefaults = new Jpeg2000CodingDefaults(mayUseSop: false, usesEph: false, Jpeg2000CodingDefaults.ProgressionLrcp, layers: 1, usesComponentTransform: false, new Jpeg2000CodingStyle(levels, blockExponent, blockExponent, codeBlockStyle: 0, isReversible: true, precincts)),
            Quantization = new Jpeg2000Quantization(Jpeg2000Quantization.StyleNone, guardBits: 2, exponents, new ushort[bandCount]),
        };
    }
}
