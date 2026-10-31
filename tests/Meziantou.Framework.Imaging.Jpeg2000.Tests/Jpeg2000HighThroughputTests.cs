using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The high-throughput block coder of Rec. ITU-T T.814 | ISO/IEC 15444-15: the code tables against Annex C, the block
/// decoder on HT segments written by hand from the decoding procedures of clause 7, and codestreams whose content is
/// known (<see cref="Jpeg2000TestFiles"/>).
/// </summary>
public sealed class Jpeg2000HighThroughputTests
{
    private const int Negative = int.MinValue;

    // An HT cleanup segment for a quad whose only significant sample is the first one, with the magnitude 1:
    // - the MagSgn byte-stream, one byte: the sign (7.3.8: one bit, since the exponent bound is 1);
    // - the MEL byte-stream, one byte: the bit 0, which is the symbol 1 of a quad that has significant samples (7.3.3);
    // - the VLC byte-stream, read from the high half of the second last byte: 0, 1, 1, 0, the codeword 0x06 of the
    //   row {0, 0x1, 0x0, 0x0, 0x0, 0x06, 4} of CxtVLC_table_0;
    // - the suffix length, 3 bytes, in the low half of the second last byte and in the last byte (7.1.1).
    private static readonly byte[] NegativeOne = [0x01, 0x00, 0x63, 0x00];
    private static readonly byte[] PositiveOne = [0x00, 0x00, 0x63, 0x00];

    // The same quad with the magnitude 2: the row {0, 0x1, 0x1, 0x1, 0x1, 0x3F, 7}, whose codeword is 1, 1, 1, 1, 1,
    // 1, 0, then the unsigned residual prefix "1" (u = 1, so that the exponent bound is 2). The high half of the second
    // last byte only gives three bits when they are all 1 (7.1.4); the five other bits are in the byte before it. The
    // sample has a known bit, which is 1 (the two patterns of the row), and one bit in the MagSgn bit-stream: the sign.
    private static readonly byte[] NegativeTwo = [0x01, 0x00, 0x17, 0x74, 0x00];

    public static TheoryData<string> AuthoredFiles =>
    [
        nameof(Jpeg2000TestFiles.HighThroughputRefined),
        nameof(Jpeg2000TestFiles.HighThroughputSignificancePropagation),
        nameof(Jpeg2000TestFiles.HighThroughputCausal),
        nameof(Jpeg2000TestFiles.HighThroughputPlaceholders),
        nameof(Jpeg2000TestFiles.HighThroughputLayers),
    ];

    [Fact]
    public void TheCodeTablesHaveTheRowsOfTheSpecification()
    {
        Assert.HasCount(4 * Jpeg2000HtVlcTables.InitialRowCount, Jpeg2000HtVlcTables.InitialRows);
        Assert.HasCount(4 * Jpeg2000HtVlcTables.NonInitialRowCount, Jpeg2000HtVlcTables.NonInitialRows);

        // Annex C: the context, the significance pattern, the unsigned residual offset, the two exponent max bound
        // patterns, the codeword and its length. The first and the last rows of each table, and rows inside them
        AssertRow(isInitialRow: true, 0, 0, 0x1, 0, 0x0, 0x0, 0x06, 4);
        AssertRow(isInitialRow: true, 1, 0, 0x1, 1, 0x1, 0x1, 0x3F, 7);
        AssertRow(isInitialRow: true, 2, 0, 0x2, 0, 0x0, 0x0, 0x00, 3);
        AssertRow(isInitialRow: true, 6, 0, 0x3, 1, 0x3, 0x1, 0x1F, 7);
        AssertRow(isInitialRow: true, 26, 0, 0xB, 1, 0x1, 0x0, 0x09, 6);
        AssertRow(isInitialRow: true, 443, 7, 0xF, 1, 0xF, 0x4, 0x08, 4);
        AssertRow(isInitialRow: false, 0, 0, 0x1, 0, 0x0, 0x0, 0x00, 3);
        AssertRow(isInitialRow: false, 1, 0, 0x1, 1, 0x1, 0x1, 0x27, 6);
        AssertRow(isInitialRow: false, 354, 7, 0xF, 1, 0xF, 0x8, 0x12, 5);
        AssertRow(isInitialRow: false, 357, 7, 0xF, 1, 0xF, 0x4, 0x1C, 5);

        static void AssertRow(bool isInitialRow, int index, int context, int pattern, int offset, int known, int ones, int codeword, int length)
        {
            var rows = isInitialRow ? Jpeg2000HtVlcTables.InitialRows : Jpeg2000HtVlcTables.NonInitialRows;
            byte[] expected = [(byte)((context << 4) | pattern), (byte)((known << 4) | ones), (byte)((offset << 7) | codeword), (byte)length];
            Assert.Equal(expected, rows.Slice(4 * index, 4).ToArray());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheCodeTablesArePrefixCodes(bool isInitialRow)
    {
        var rows = isInitialRow ? Jpeg2000HtVlcTables.InitialRows : Jpeg2000HtVlcTables.NonInitialRows;
        var firstRow = isInitialRow ? 0 : Jpeg2000HtVlcTables.InitialRowCount;
        Span<int> occupied = stackalloc int[8];
        occupied.Clear();
        var previous = -1;
        for (var row = 0; row < rows.Length / 4; row++)
        {
            var context = rows[4 * row] >> 4;
            var pattern = rows[4 * row] & 0x0F;
            var known = rows[(4 * row) + 1] >> 4;
            var ones = rows[(4 * row) + 1] & 0x0F;
            var offset = rows[(4 * row) + 2] >> 7;
            var codeword = rows[(4 * row) + 2] & 0x7F;
            int length = rows[(4 * row) + 3];

            // The rows are in the order of the contexts, and a context has eight of them
            Assert.True(context >= previous && context < 8);
            previous = context;
            Assert.InRange(length, 1, Jpeg2000HtVlcTables.MaxCodewordLength);
            Assert.True(codeword < 1 << length);

            // 7.3.5: in the context 0, a quad without a significant sample is a MEL symbol and has no row; the samples
            // of the patterns are significant ones, and a known bit that is 1 is a known bit; without an unsigned
            // residual, no bit is known
            if (context == 0)
            {
                Assert.NotEqual(0, pattern);
            }

            Assert.Equal(0, known & ~pattern);
            Assert.Equal(0, ones & ~known);
            if (offset == 0)
            {
                Assert.Equal(0, known);
            }

            // Kraft: the codewords of a context fill the code space, each sequence of bits once
            occupied[context] += 1 << (Jpeg2000HtVlcTables.MaxCodewordLength - length);

            // The codeword, followed by any bits, is decoded to its row
            for (var following = 0; following < 1 << (Jpeg2000HtVlcTables.MaxCodewordLength - length); following++)
            {
                var entry = Jpeg2000HtVlcTables.Find(isInitialRow, context, codeword | (following << length));
                Assert.Equal((uint)(length | (pattern << 3) | (offset << 7) | (known << 8) | (ones << 12) | ((firstRow + row) << 16)), entry);
            }
        }

        foreach (var count in occupied)
        {
            Assert.Equal(1 << Jpeg2000HtVlcTables.MaxCodewordLength, count);
        }
    }

    [Fact]
    public void ACleanupPassGivesTheMagnitudeAndTheSign()
    {
        // The coefficients have twice their magnitude plus one: the half of the bit-plane below the last one
        Assert.Equal([Negative | 3], Decode(1, 1, bitPlanes: 8, skippedBitPlanes: 7, passCount: 1, NegativeOne, []));
        Assert.Equal([3], Decode(1, 1, bitPlanes: 8, skippedBitPlanes: 7, passCount: 1, PositiveOne, []));
        Assert.Equal([Negative | 3, 0, 0, 0], Decode(2, 2, bitPlanes: 8, skippedBitPlanes: 7, passCount: 1, NegativeOne, []));
        Assert.Equal([Negative | 5, 0, 0, 0], Decode(2, 2, bitPlanes: 8, skippedBitPlanes: 7, passCount: 1, NegativeTwo, []));

        // 7.6: the pass gives the bit-planes above the skipped ones, wherever they are in the sub-band
        Assert.Equal([Negative | (3 << 7)], Decode(1, 1, bitPlanes: 8, skippedBitPlanes: 0, passCount: 1, NegativeOne, []));
        Assert.Equal([5 << 3, 0, 0, 0], Decode(2, 2, bitPlanes: 5, skippedBitPlanes: 1, passCount: 1, [0x00, 0x00, 0x17, 0x74, 0x00], []));

        // Without the byte of the MagSgn byte-stream, its bits are 1 (7.1.2)
        Assert.Equal([Negative | 3], Decode(1, 1, bitPlanes: 8, skippedBitPlanes: 7, passCount: 1, [0x00, 0x63, 0x00], []));
    }

    [Fact]
    public void TheRefinementPassesGiveTheNextBitPlane()
    {
        // The code-block has the samples a b / c d, and a has the magnitude 1 after the HT cleanup pass, one bit-plane
        // above the last one. The HT SigProp pass visits c, b and d, which all have a as a neighbor (7.4): the bits 1, 0
        // and 1, then the signs of c (negative) and d: 1, 0, 1, 1, 0, the first one in the least significant bit. The HT
        // MagRef pass gives one bit to a, in the last byte (7.5).
        byte[] refinement = [0x0D, 0x01];
        Assert.Equal([Negative | 6, 0, 0, 0], Decode(2, 2, bitPlanes: 8, skippedBitPlanes: 6, passCount: 1, NegativeOne, []));
        Assert.Equal([Negative | 6, 0, Negative | 3, 3], Decode(2, 2, bitPlanes: 8, skippedBitPlanes: 6, passCount: 2, NegativeOne, refinement.AsSpan(0, 1)));
        Assert.Equal([Negative | 7, 0, Negative | 3, 3], Decode(2, 2, bitPlanes: 8, skippedBitPlanes: 6, passCount: 3, NegativeOne, refinement));

        // The bit 0 leaves a with the magnitude 2; past the end of the HT refinement segment the bits are 0
        Assert.Equal([Negative | 5, 0, Negative | 3, 3], Decode(2, 2, bitPlanes: 8, skippedBitPlanes: 6, passCount: 3, NegativeOne, [0x0D, 0x00]));
        Assert.Equal([Negative | 5, 0, 0, 0], Decode(2, 2, bitPlanes: 8, skippedBitPlanes: 6, passCount: 3, NegativeOne, []));

    }

    [Fact]
    public void VerticallyCausalContextsIgnoreTheNextStripe()
    {
        // A column of six samples, whose fifth one, the first of the second stripe, has the magnitude 1. Its quad is the
        // third one: the MEL bits 1, 1 (two quads without a significant sample) and 0, then the codeword 0, 0, 0 of the
        // row {0, 0x1, 0x0, 0x0, 0x0, 0x00, 3} of CxtVLC_table_1, and the sign in the MagSgn byte-stream.
        byte[] cleanup = [0x00, 0xC0, 0x03, 0x00];
        Assert.Equal([0, 0, 0, 0, 6, 0], Decode(1, 6, bitPlanes: 8, skippedBitPlanes: 6, passCount: 1, cleanup, []));

        // The HT SigProp bits 1, 1, 1, 0. The fourth sample has the fifth one as a neighbor: it takes a bit and a sign,
        // then the sixth sample takes a bit and a sign. With the vertically causal contexts, the fourth sample ignores
        // the next stripe and takes no bit: the first two bits go to the sixth sample
        Assert.Equal([0, 0, 0, Negative | 3, 6, 3], Decode(1, 6, bitPlanes: 8, skippedBitPlanes: 6, passCount: 2, cleanup, [0x07]));
        Assert.Equal([0, 0, 0, 0, 6, Negative | 3], Decode(1, 6, bitPlanes: 8, skippedBitPlanes: 6, passCount: 2, cleanup, [0x07], isVerticallyCausal: true));
    }

    [Fact]
    public void MalformedSegmentsAreInvalid()
    {
        // 7.1.1: the suffix length is between 2 and the length of the segment
        AssertInvalid([0x01, 0x00, 0x61, 0x00]);
        AssertInvalid([0x01, 0x00, 0x65, 0x00]);
        AssertInvalid([0x01, 0x00, 0x63, 0x01]);
        AssertInvalid([0x00]);

        // A quad of four significant samples with the exponent bound 4: the row {0, 0xF, 0x1, 0xF, 0x8, 0x39, 6}, whose
        // codeword is 1, 0, 0, 1, 1, 1, then the unsigned residual 3 (the prefix 0, 0, 1 and the suffix 0). Each sample
        // has a known bit, which is 1 for the last one, and three bits in the MagSgn bit-stream: with zeros, the
        // magnitudes are 1, 1, 1 and 5
        Assert.Equal([3, 3, 3, 11], Decode(2, 2, bitPlanes: 8, skippedBitPlanes: 7, passCount: 1, [0x00, 0x00, 0x00, 0x13, 0x94, 0x00], []));

        // 7.1.2: the MagSgn bit-stream has one byte more than its byte-stream, and no more
        Assert.Throws<InvalidImageContentException>(() => Decode(2, 2, bitPlanes: 8, skippedBitPlanes: 7, passCount: 1, [0x00, 0x13, 0x94, 0x00], []));

        // 7.1.4: the VLC byte-stream does not extend into the MagSgn byte-stream. The codeword of the magnitude 2
        // without its last bits: its first three bits are in the second last byte, and the suffix has two bytes
        Assert.Throws<InvalidImageContentException>(() => Decode(2, 2, bitPlanes: 8, skippedBitPlanes: 7, passCount: 1, [0x01, 0x72, 0x00], []));

        // 7.6: a magnitude has the bit-planes above the skipped ones, and one more for the refinement passes
        Assert.Throws<InvalidImageContentException>(() => Decode(2, 2, bitPlanes: 8, skippedBitPlanes: 0, passCount: 1, NegativeTwo, []));
        Assert.Throws<InvalidImageContentException>(() => Decode(1, 1, bitPlanes: 8, skippedBitPlanes: 8, passCount: 1, NegativeOne, []));
        Assert.Throws<InvalidImageContentException>(() => Decode(1, 1, bitPlanes: 8, skippedBitPlanes: 7, passCount: 2, NegativeOne, [0x00]));

        // 7.2: the second sample of the quad is not in a code-block of one row. The row {0, 0x2, 0x0, 0x0, 0x0, 0x00, 3}
        Assert.Throws<InvalidImageContentException>(() => Decode(2, 1, bitPlanes: 8, skippedBitPlanes: 7, passCount: 1, [0x00, 0x00, 0x03, 0x00], []));

        static void AssertInvalid(byte[] cleanup)
            => Assert.Throws<InvalidImageContentException>(() => Decode(1, 1, bitPlanes: 8, skippedBitPlanes: 7, passCount: 1, cleanup, []));
    }

    [Theory]
    [MemberData(nameof(AuthoredFiles))]
    public void RefinementAndPlaceholderPassesDecodeToTheCodedCoefficients(string file)
    {
        using var image = Image.Load(GetFile(file));
        Assert.Equal(PixelFormat.Gray8, image.PixelFormat);
        var expected = file switch
        {
            nameof(Jpeg2000TestFiles.HighThroughputRefined) => Jpeg2000TestFiles.HighThroughputRefinedSamples,
            nameof(Jpeg2000TestFiles.HighThroughputSignificancePropagation) => Jpeg2000TestFiles.HighThroughputSignificancePropagationSamples,
            nameof(Jpeg2000TestFiles.HighThroughputCausal) => Jpeg2000TestFiles.HighThroughputCausalSamples,
            nameof(Jpeg2000TestFiles.HighThroughputPlaceholders) => Jpeg2000TestFiles.HighThroughputPlaceholdersSamples,
            _ => Jpeg2000TestFiles.HighThroughputLayersSamples,
        };

        Assert.Equal(expected, GetBytes(image));
    }

    [Fact]
    public void TheVerticallyCausalBitChangesTheSignificancePropagationPass()
    {
        // Without the bit 3 of the code-block style, the HT SigProp pass reads other bits of the same codestream
        var data = Jpeg2000TestFiles.HighThroughputCausal;
        var style = FindCodeBlockStyle(data);
        Assert.Equal(0x48, data[style]);
        data[style] = 0x40;
        byte[]? pixels = null;
        try
        {
            using var image = Image.Load(data);
            pixels = GetBytes(image);
        }
        catch (InvalidImageContentException)
        {
        }

        Assert.NotEqual(Jpeg2000TestFiles.HighThroughputCausalSamples, pixels);
    }

    [Fact]
    public void TheCapabilitiesAreNeeded()
    {
        // The style of a codestream that does not declare Rec. ITU-T T.814: no bit of Rsiz, no CAP marker segment
        var data = Jpeg2000TestFiles.HighThroughputGray;
        Assert.Equal(0x51, data[3]);
        Assert.Equal(0x40, data[6]);
        var capabilities = data.AsSpan().IndexOf<byte>([0xFF, 0x50, 0x00, 0x08]);
        Assert.True(capabilities > 0);
        data[6] = 0;
        byte[] withoutCapabilities = [.. data[..capabilities], .. data[(capabilities + 10)..]];
        Assert.Throws<InvalidImageContentException>(() => Image.Load(withoutCapabilities));

        // The two declarations go together
        Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
        data[6] = 0x40;
        byte[] withoutMarker = [.. data[..capabilities], .. data[(capabilities + 10)..]];
        Assert.Throws<InvalidImageContentException>(() => Image.Load(withoutMarker));
    }

    [Fact]
    public void UnsupportedArrangementsAreReported()
    {
        // T.814, Table A.4: each code-block chooses its block coder
        var mixed = Jpeg2000TestFiles.HighThroughputGray;
        mixed[FindCodeBlockStyle(mixed)] = 0xC0;
        Assert.Equal("mixed block coders", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(mixed)).Feature);

        // The bit 7 alone is reserved
        var reserved = Jpeg2000TestFiles.HighThroughputGray;
        reserved[FindCodeBlockStyle(reserved)] = 0x80;
        Assert.Throws<InvalidImageContentException>(() => Image.Load(reserved));

        Assert.Equal("several HT sets", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Jpeg2000TestFiles.HighThroughputSeveralSets)).Feature);
        Assert.Equal("placeholder passes over several packets", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(Jpeg2000TestFiles.HighThroughputPlaceholdersAlone)).Feature);
    }

    [Fact]
    public void ACorruptedCleanupSegmentIsInvalid()
    {
        // The last bytes of the tile are the suffix length of the HT cleanup segment of its last code-block: before the
        // EOC marker
        var data = Jpeg2000TestFiles.HighThroughputGray;
        Assert.Equal(0xD9, data[^1]);
        data[^3] = 0xFF;
        Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(1, 1, 9)]
    [InlineData(2, 2, 3)]
    [InlineData(3, 5, 8)]
    [InlineData(7, 1, 12)]
    [InlineData(1, 9, 5)]
    [InlineData(17, 13, 16)]
    [InlineData(32, 32, 20)]
    [InlineData(64, 64, 8)]
    [InlineData(64, 64, 30)]
    [InlineData(1024, 4, 10)]
    [InlineData(4, 1024, 10)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void EachHtCleanupSegmentOfTheEncoderDecodesToItsBitPlanes(int width, int height, int bitPlanes)
    {
        var scope = new AllocationScope(new ImageResourceLimits());
        using var encoder = new Jpeg2000HtBlockEncoder(scope);
        using var decoder = new Jpeg2000HtBlockDecoder(scope);
        using var output = new PooledPayloadWriter(scope);
        var samples = new int[width * height];
        var decoded = new int[width * height];
        var candidates = new Jpeg2000CodingPass[Jpeg2000Tier1Encoder.MaxPasses];
        for (var seed = 0; seed < 16; seed++)
        {
            var random = new Random((seed * 1000) + width + (height * 3) + bitPlanes);
            var isIrreversible = seed % 2 == 1;

            // Dense noise, sparse coefficients (long MEL runs), regions of large magnitudes (residuals above 2 in the first
            // row of quads), and magnitudes that are all the largest one
            for (var i = 0; i < samples.Length; i++)
            {
                var magnitude = (seed % 4) switch
                {
                    0 => random.Next(1 << Math.Min(bitPlanes, 30)),
                    1 => random.Next(40) == 0 ? random.Next(1 << Math.Min(bitPlanes, 30)) : 0,
                    2 => (i % width) < width / 2 ? random.Next(1 << Math.Min(bitPlanes, 30)) : random.Next(1 << (bitPlanes / 3)),
                    _ => (1 << (bitPlanes - 1)) | (i % 3),
                };
                samples[i] = random.Next(2) == 0 || magnitude == 0 ? magnitude : magnitude | int.MinValue;
            }

            samples[random.Next(samples.Length)] = (1 << (bitPlanes - 1)) | (random.Next(2) == 0 ? int.MinValue : 0);
            output.Reset();
            Assert.Equal(bitPlanes, encoder.Encode(samples, width, height, output, out var length, candidates, isIrreversible));
            var data = output.WrittenSpan.ToArray();
            Assert.HasCount(length, data);

            // One segment for each lowest bit-plane, from the highest one; each one decodes to the coefficients down to
            // its bit-plane, reconstructed in the middle of the others (7.6). The sub-band has one bit-plane more than the
            // coefficients, as the encoder declares it
            var offset = 0;
            var reduction = 0.0;
            for (var i = 0; i < bitPlanes; i++)
            {
                var plane = bitPlanes - 1 - i;
                var segment = data.AsSpan(offset, candidates[i].Length).ToArray();
                offset += segment.Length;
                AssertNoMarker(segment, $"{width}x{height}, seed {seed}, plane {plane}");
                decoder.Decode(decoded, width, height, isVerticallyCausal: false, bitPlanes + 1, skippedBitPlanes: bitPlanes - plane, passCount: 1, segment, 0, segment.Length, 0, 0);
                var error = 0.0;
                var reference = 0.0;
                for (var n = 0; n < samples.Length; n++)
                {
                    var magnitude = samples[n] & int.MaxValue;
                    var expected = magnitude >> plane == 0 ? 0 : (int)((uint)(((2 * (magnitude >> plane)) + 1) << plane) | ((uint)samples[n] & 0x80000000));
                    if (decoded[n] != expected)
                        Assert.Fail($"{width}x{height}, seed {seed}, plane {plane}: the sample {n} is {decoded[n]:X8} instead of {expected:X8}.");

                    var value = magnitude == 0 ? 0 : magnitude + (isIrreversible ? 0.5 : 0);
                    var twice = decoded[n] & int.MaxValue;
                    var reconstruction = isIrreversible || plane > 0 ? twice / 2.0 : twice / 2;
                    error += Math.Pow(value - reconstruction, 2);
                    reference += value * value;
                }

                reduction += candidates[i].Distortion;
                Assert.Equal(reference - error, reduction, Math.Max(1e-6, reference * 1e-12));
            }

            Assert.Equal(length, offset);

            // Without candidates, the segment of the bit-plane 0 alone
            output.Reset();
            Assert.Equal(bitPlanes, encoder.Encode(samples, width, height, output, out var single));
            Assert.Equal(data.AsSpan(length - candidates[bitPlanes - 1].Length).ToArray(), output.WrittenSpan.ToArray());
            Assert.Equal(candidates[bitPlanes - 1].Length, single);
        }

        static void AssertNoMarker(byte[] segment, string context)
        {
            // The segment never holds an FF byte followed by a byte above 8F, and does not end with an FF byte
            Assert.NotEqual(0xFF, segment[^1]);
            for (var i = 0; i + 1 < segment.Length; i++)
            {
                if (segment[i] == 0xFF && segment[i + 1] > 0x8F)
                    Assert.Fail($"{context}: the bytes {i} and {i + 1} are FF {segment[i + 1]:X2}.");
            }
        }
    }

    [Fact]
    public void ACodeBlockOfZerosIsNotCodedByTheHtEncoder()
    {
        var scope = new AllocationScope(new ImageResourceLimits());
        using var encoder = new Jpeg2000HtBlockEncoder(scope);
        using var output = new PooledPayloadWriter(scope);
        Assert.Equal(0, encoder.Encode(new int[16], 4, 4, output, out var length));
        Assert.Equal((0, 0), (length, output.Length));
    }

    private static int[] Decode(int width, int height, int bitPlanes, int skippedBitPlanes, int passCount, ReadOnlySpan<byte> cleanup, ReadOnlySpan<byte> refinement, bool isVerticallyCausal = false)
    {
        var scope = new AllocationScope(new ImageResourceLimits());
        using var decoder = new Jpeg2000HtBlockDecoder(scope);
        byte[] data = [0xAA, .. cleanup, .. refinement, 0xAA];
        var samples = new int[width * height];
        samples.AsSpan().Fill(-1);
        decoder.Decode(samples, width, height, isVerticallyCausal, bitPlanes, skippedBitPlanes, passCount, data, 1, cleanup.Length, 1 + cleanup.Length, refinement.Length);
        return samples;
    }

    // SPcod of the COD marker segment: the code-block style follows the number of decomposition levels and the two
    // exponents of the code-block size, after Scod and the four bytes of SGcod
    private static int FindCodeBlockStyle(ReadOnlySpan<byte> data) => data.IndexOf<byte>([0xFF, 0x52, 0x00, 0x0C]) + 12;

    private static byte[] GetBytes(Image image)
    {
        var bytes = new byte[image.Width * image.Height * PixelFormats.GetBytesPerPixel(image.PixelFormat)];
        image.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }

    private static byte[] GetFile(string name) => name switch
    {
        nameof(Jpeg2000TestFiles.HighThroughputRefined) => Jpeg2000TestFiles.HighThroughputRefined,
        nameof(Jpeg2000TestFiles.HighThroughputSignificancePropagation) => Jpeg2000TestFiles.HighThroughputSignificancePropagation,
        nameof(Jpeg2000TestFiles.HighThroughputCausal) => Jpeg2000TestFiles.HighThroughputCausal,
        nameof(Jpeg2000TestFiles.HighThroughputPlaceholders) => Jpeg2000TestFiles.HighThroughputPlaceholders,
        nameof(Jpeg2000TestFiles.HighThroughputLayers) => Jpeg2000TestFiles.HighThroughputLayers,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown file."),
    };
}
