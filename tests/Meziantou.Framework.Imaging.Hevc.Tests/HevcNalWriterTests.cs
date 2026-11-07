using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Hevc;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The writer of NAL units (Rec. ITU-T H.265, clauses 7.3.1.1, 7.3.1.2 and 7.4.2.3; Annex B; ISO/IEC 14496-15): the
/// expected bytes are worked out by hand from the encapsulation procedure of the specification, and the NAL units are read
/// back by the decoder (<see cref="HevcNalUnitHeader"/>, <see cref="HevcRbspBuffer"/>) and by the reader of the test
/// harness (<see cref="ReferenceHevcStream"/>), which shares no code with the library.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class HevcNalWriterTests
{
    [Fact]
    public void TheHeaderIsTheTypeAndTheTemporalIdentifierOfTheBaseLayer()
    {
        // 0 100001 000000 001: a sequence parameter set, TemporalId 0
        Assert.Equal([0x42, 0x01, 0xAB], WriteNalUnit(HevcConstants.NalSps, 0, [0xAB]));

        // 0 000001 000000 011: TRAIL_R, TemporalId 2; 0 101000 000000 001: a suffix SEI NAL unit
        Assert.Equal([0x02, 0x03, 0x80], WriteNalUnit(HevcConstants.NalTrailR, 2, [0x80]));
        Assert.Equal([0x50, 0x01, 0x80], WriteNalUnit(HevcConstants.NalSuffixSei, 0, [0x80]));
        for (var nalUnitType = 0; nalUnitType < 64; nalUnitType++)
        {
            for (var temporalId = 0; temporalId < 7; temporalId++)
            {
                var header = HevcNalUnitHeader.Read(WriteNalUnit(nalUnitType, temporalId, [0x80]));
                Assert.Equal((nalUnitType, 0, temporalId), (header.NalUnitType, header.NuhLayerId, header.TemporalId));
            }
        }
    }

    [Theory]

    // Two zero bytes followed by a byte of at most 3: a byte equal to 3 is inserted before it
    [InlineData("00000080", "0000030080")]
    [InlineData("000001", "00000301")]
    [InlineData("000002", "00000302")]
    [InlineData("000003", "00000303")]
    [InlineData("AA000001BB", "AA00000301BB")]

    // A byte of more than 3, one zero byte, or zero bytes that are not together: nothing is inserted
    [InlineData("000004", "000004")]
    [InlineData("0000FF", "0000FF")]
    [InlineData("00010001", "00010001")]
    [InlineData("00030003", "00030003")]
    [InlineData("0001", "0001")]

    // The byte that follows an inserted byte counts for the next search
    [InlineData("0000000001", "0000030000 0301")]
    [InlineData("000000000000", "000003000003000003")]
    [InlineData("0000010000020000030000", "000003010000030200000303000003")]
    [InlineData("00000003000001", "000003000300000301")]

    // A payload that ends with a cabac_zero_word: a NAL unit does not end with a zero byte
    [InlineData("800000", "8000 0003")]
    [InlineData("80000000 00", "80000003 000003")]
    [InlineData("0000", "000003")]

    // No payload at all: the NAL units of the end of a sequence and of a bitstream
    [InlineData("", "")]
    public void EmulationPreventionBytesAreInsertedAsClause7423Says(string rbsp, string expected)
    {
        var payload = Convert.FromHexString(rbsp.Replace(" ", "", StringComparison.Ordinal));
        var nalUnit = WriteNalUnit(HevcConstants.NalPrefixSei, 0, payload);
        Assert.Equal("4E01" + expected.Replace(" ", "", StringComparison.Ordinal), Convert.ToHexString(nalUnit));
        AssertReadBack(nalUnit, payload);
    }

    [Fact]
    public void PayloadsFullOfZeroBytesAreReadBack()
    {
        // The bytes are drawn among 0 to 4, with many zero bytes: every pattern of the encapsulation, at every position
        for (var seed = 1; seed <= 300; seed++)
        {
            var random = new Random(seed);
            var payload = new byte[random.Next(1, 200)];
            var zeroWeight = random.Next(1, 10);
            for (var i = 0; i < payload.Length; i++)
            {
                payload[i] = random.Next(10) < zeroWeight ? (byte)0 : (byte)random.Next(seed % 3 is 0 ? 256 : 5);
            }

            // A payload ends with its trailing bits, or with zero bytes in pairs after them
            var trailingZeros = payload.Length - 1 - payload.AsSpan().LastIndexOfAnyExcept((byte)0);
            if (seed % 4 is 0 || (trailingZeros & 1) is 1)
            {
                payload[^1] = 0x80;
            }

            AssertReadBack(WriteNalUnit(HevcConstants.NalTrailR, 0, payload), payload);
        }

        // Long runs of each byte value that is escaped
        for (var value = 0; value <= 3; value++)
        {
            var payload = new byte[1000];
            payload.AsSpan().Fill((byte)value);
            payload[0] = 0;
            payload[1] = 0;
            payload[^1] = 0x80;
            AssertReadBack(WriteNalUnit(HevcConstants.NalTrailR, 0, payload), payload);
        }
    }

    [Fact]
    public void LengthPrefixedNalUnitsAreReadByTheDecoder()
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(LengthPrefixedNalUnitsAreReadByTheDecoder));
        using var output = new PooledPayloadWriter(scope);
        (int Type, byte[] Payload)[] units =
        [
            (HevcConstants.NalVps, [0x0C, 0x01, 0xFF, 0xFF, 0x80]),
            (HevcConstants.NalSps, [0x00, 0x00, 0x00, 0x00, 0x01, 0x80]),
            (HevcConstants.NalIdrNLp, [.. Enumerable.Range(0, 70000).Select(i => (byte)(i % 7 is 0 ? 0 : i))]),
            (HevcConstants.NalSuffixSei, [0x84, 0x00, 0x80]),
        ];
        var lengths = new List<int>();
        foreach (var (type, payload) in units)
        {
            lengths.Add(HevcNalWriter.WriteLengthPrefixed(output, type, 0, payload));
        }

        // The first length: 00 00 00 07, the two bytes of the header and the five bytes of the payload
        var data = output.WrittenSpan.ToArray();
        Assert.Equal([0x00, 0x00, 0x00, 0x07, 0x40, 0x01, 0x0C], data[..7]);
        var position = 0;
        using var buffer = new HevcRbspBuffer(scope);
        for (var i = 0; i < units.Length; i++)
        {
            Assert.True(HevcNalUnitHeader.TryReadNext(data, HevcNalWriter.NalLengthSize, ref position, out var offset, out var length));
            Assert.Equal(lengths[i], length);
            Assert.Equal(units[i].Type, HevcNalUnitHeader.Read(data.AsSpan(offset, length)).NalUnitType);
            buffer.Load(data, offset + HevcNalUnitHeader.Length, length - HevcNalUnitHeader.Length);
            Assert.True(buffer.Span.SequenceEqual(units[i].Payload));
        }

        Assert.False(HevcNalUnitHeader.TryReadNext(data, HevcNalWriter.NalLengthSize, ref position, out _, out _));

        // The reader of the test harness sees the same NAL units
        var split = ReferenceHevcStream.SplitLengthPrefixed(data);
        Assert.Equal(units.Length, split.Count);
        for (var i = 0; i < units.Length; i++)
        {
            Assert.Equal(units[i].Type, ReferenceHevcStream.GetNalUnitType(split[i].Span));
            Assert.Equal(units[i].Payload, ReferenceHevcStream.GetRbsp(split[i].Span));
        }
    }

    [Fact]
    public void ByteStreamNalUnitsFollowAStartCode()
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(ByteStreamNalUnitsFollowAStartCode));
        using var output = new PooledPayloadWriter(scope);
        byte[][] payloads = [[0x0C, 0x80], [0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x80], [0xFF, 0x00, 0x00, 0x02, 0x80]];
        Assert.Equal(4, HevcNalWriter.WriteByteStream(output, HevcConstants.NalVps, 0, payloads[0]));
        Assert.Equal(11, HevcNalWriter.WriteByteStream(output, HevcConstants.NalSps, 0, payloads[1]));
        Assert.Equal(8, HevcNalWriter.WriteByteStream(output, HevcConstants.NalPps, 0, payloads[2]));
        var data = output.WrittenSpan.ToArray();

        // zero_byte, start_code_prefix_one_3bytes, then the NAL unit
        Assert.Equal([0x00, 0x00, 0x00, 0x01, 0x40, 0x01, 0x0C, 0x80, 0x00, 0x00, 0x00, 0x01, 0x42, 0x01, 0x00, 0x00, 0x03, 0x01, 0x00, 0x00, 0x03, 0x00, 0x80, 0x00, 0x00, 0x00, 0x01, 0x44, 0x01, 0xFF, 0x00, 0x00, 0x03, 0x02, 0x80], data);
        var split = ReferenceHevcStream.SplitAnnexB(data);
        Assert.Equal(3, split.Count);
        for (var i = 0; i < payloads.Length; i++)
        {
            Assert.Equal(HevcConstants.NalVps + i, ReferenceHevcStream.GetNalUnitType(split[i].Span));
            Assert.Equal(payloads[i], ReferenceHevcStream.GetRbsp(split[i].Span));
        }
    }

    // The payload of a NAL unit is the one that was written, for the decoder and for the reader of the test harness, and
    // the NAL unit has no start code prefix and no three zero bytes
    private static void AssertReadBack(byte[] nalUnit, byte[] payload)
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(HevcNalWriterTests));
        using var buffer = new HevcRbspBuffer(scope);
        var message = Convert.ToHexString(payload);
        Assert.True(buffer.Load(nalUnit.AsSpan(HevcNalUnitHeader.Length)).SequenceEqual(payload), message);
        Assert.Equal(nalUnit.Length - HevcNalUnitHeader.Length - payload.Length, buffer.EmulationPreventionByteCount, message);
        Assert.Equal(payload, ReferenceHevcStream.GetRbsp(nalUnit), message);

        // Clause 7.4.2.1: no 00 00 00, 00 00 01 or 00 00 02 at any byte position; 00 00 03 is followed by a byte of at
        // most 3 or by nothing; the last byte is not zero
        for (var i = 0; i + 2 < nalUnit.Length; i++)
        {
            if (nalUnit[i] is 0 && nalUnit[i + 1] is 0)
            {
                Assert.True(nalUnit[i + 2] >= 3, message);
                Assert.True(nalUnit[i + 2] > 3 || i + 3 == nalUnit.Length || nalUnit[i + 3] <= 3, message);
            }
        }

        Assert.True(nalUnit[^1] is not 0, message);

        // The same bytes in the two framings
        using var output = new PooledPayloadWriter(scope);
        Assert.Equal(nalUnit.Length, HevcNalWriter.WriteLengthPrefixed(output, nalUnit[0] >> 1, (nalUnit[1] & 7) - 1, payload));
        Assert.Equal(nalUnit.Length, HevcNalWriter.WriteByteStream(output, nalUnit[0] >> 1, (nalUnit[1] & 7) - 1, payload));
        var written = output.WrittenSpan;
        Assert.Equal((uint)nalUnit.Length, System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(written));
        Assert.True(written.Slice(4, nalUnit.Length).SequenceEqual(nalUnit), message);
        Assert.True(written.Slice(4 + nalUnit.Length, 4).SequenceEqual((ReadOnlySpan<byte>)[0, 0, 0, 1]), message);
        Assert.True(written[(8 + nalUnit.Length)..].SequenceEqual(nalUnit), message);
    }

    private static byte[] WriteNalUnit(int nalUnitType, int temporalId, byte[] rbsp)
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(HevcNalWriterTests));
        using var output = new PooledPayloadWriter(scope);
        Assert.Equal(HevcNalUnitHeader.Length, HevcNalWriter.WriteNalUnit(output, nalUnitType, temporalId, []));
        output.Reset();
        var length = HevcNalWriter.WriteNalUnit(output, nalUnitType, temporalId, rbsp);
        Assert.Equal(output.Length, length);
        return output.WrittenSpan.ToArray();
    }
}
