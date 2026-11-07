using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Tests.Conformance.Hardening;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// NAL unit headers, length-prefixed NAL units and emulation prevention (Rec. ITU-T H.265, clauses 7.3.1.1 and 7.3.1.2;
/// ISO/IEC 14496-15): the expected values are worked out by hand on bytes written out in the tests.
/// </summary>
public sealed class HevcNalUnitTests
{
    [Fact]
    public void TheHeaderIsTwoBytes()
    {
        // 0 100001 000000 001: a sequence parameter set of the base layer, TemporalId 0
        var sps = HevcNalUnitHeader.Read([0x42, 0x01, 0xFF]);
        Assert.Equal((HevcConstants.NalSps, 0, 0), (sps.NalUnitType, sps.NuhLayerId, sps.TemporalId));
        Assert.False(sps.IsVcl);

        // 0 010011 000101 011: an IDR_W_RADL slice segment of the layer 5, TemporalId 2
        var idr = HevcNalUnitHeader.Read([0x26, 0x2B]);
        Assert.Equal((HevcConstants.NalIdrWRadl, 5, 2), (idr.NalUnitType, idr.NuhLayerId, idr.TemporalId));
        Assert.True(idr.IsVcl && idr.IsIrap && idr.IsIdr);
        Assert.False(idr.IsRasl || idr.IsRadl || idr.IsSubLayerNonReference);

        // 0 001000 000000 001: RASL_N, a sub-layer non-reference picture; 0 001001: RASL_R, a reference picture
        Assert.True(HevcNalUnitHeader.Read([0x10, 0x01]) is { IsRasl: true, IsSubLayerNonReference: true, IsIrap: false });
        Assert.True(HevcNalUnitHeader.Read([0x12, 0x01]) is { IsRasl: true, IsSubLayerNonReference: false });

        // 0 010101: CRA_NUT is an IRAP picture that is not an IDR picture
        Assert.True(HevcNalUnitHeader.Read([0x2A, 0x01]) is { IsIrap: true, IsIdr: false, NalUnitType: HevcConstants.NalCra });
    }

    [Fact]
    public void AForbiddenHeaderIsInvalid()
    {
        // forbidden_zero_bit is 1
        Assert.Throws<InvalidImageContentException>(() => HevcNalUnitHeader.Read([0xC2, 0x01]));

        // nuh_temporal_id_plus1 is 0
        Assert.Throws<InvalidImageContentException>(() => HevcNalUnitHeader.Read([0x42, 0x00]));

        // One byte
        Assert.Throws<InvalidImageContentException>(() => HevcNalUnitHeader.Read([0x42]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void LengthPrefixedNalUnitsAreReadInOrder(int nalLengthSize)
    {
        byte[][] units = [[0x40, 0x01, 0xAA], [0x42, 0x01], [0x26, 0x01, 1, 2, 3, 4, 5]];
        var data = new List<byte> { 0xEE };
        foreach (var unit in units)
        {
            for (var i = nalLengthSize - 1; i >= 0; i--)
            {
                data.Add((byte)(unit.Length >> (8 * i)));
            }

            data.AddRange(unit);
        }

        var bytes = data.ToArray();
        var position = 1;
        foreach (var unit in units)
        {
            Assert.True(HevcNalUnitHeader.TryReadNext(bytes, nalLengthSize, ref position, out var offset, out var length));
            Assert.Equal(unit, bytes.AsSpan(offset, length).ToArray());
        }

        Assert.Equal(bytes.Length, position);
        Assert.False(HevcNalUnitHeader.TryReadNext(bytes, nalLengthSize, ref position, out _, out _));
    }

    [Fact]
    public void ALengthBeyondTheDataIsATruncation()
    {
        // The length is cut
        var position = 0;
        Assert.Throws<InvalidImageContentException>(() => HevcNalUnitHeader.TryReadNext([0, 0, 0], 4, ref position, out _, out _));

        // The NAL unit is cut: 5 bytes announced, 4 present
        position = 0;
        Assert.Throws<InvalidImageContentException>(() => HevcNalUnitHeader.TryReadNext([0, 0, 0, 5, 0x40, 0x01, 1, 2], 4, ref position, out _, out _));

        // A length of 4 GiB does not wrap around
        position = 0;
        Assert.Throws<InvalidImageContentException>(() => HevcNalUnitHeader.TryReadNext([0xFF, 0xFF, 0xFF, 0xFF, 0x40, 0x01], 4, ref position, out _, out _));

        // A NAL unit has its two header bytes
        position = 0;
        Assert.Throws<InvalidImageContentException>(() => HevcNalUnitHeader.TryReadNext([1, 0x40], 1, ref position, out _, out _));
        position = 0;
        Assert.Throws<InvalidImageContentException>(() => HevcNalUnitHeader.TryReadNext([0, 0x40], 1, ref position, out _, out _));
    }

    [Fact]
    public void APayloadWithoutEmulationPreventionIsReadInPlace()
    {
        using var audit = new PoolAudit();
        var scope = new AllocationScope(ImageResourceLimits.Default, audit.Pool, nameof(APayloadWithoutEmulationPreventionIsReadInPlace));
        using (var buffer = new HevcRbspBuffer(scope))
        {
            // 00 00 04 and 00 03 are not emulation prevention
            byte[] data = [9, 9, 0x00, 0x00, 0x04, 0x00, 0x03, 0x01, 9];
            buffer.Load(data, 2, 6);
            Assert.Same(data, buffer.Array);
            Assert.Equal((2, 6, 0), (buffer.Offset, buffer.Length, buffer.EmulationPreventionByteCount));
            Assert.Equal(new byte[] { 0x00, 0x00, 0x04, 0x00, 0x03, 0x01 }, buffer.Span.ToArray());
            Assert.Equal(3, buffer.FromSourceOffset(3));
            Assert.Equal(3, buffer.ToSourceOffset(3));
            Assert.Equal(0, audit.Rentals);

            ReadOnlySpan<byte> span = [0x12, 0x00, 0x00, 0x01];
            Assert.True(span == buffer.Load(span));
        }

        Assert.Null(audit.Verify());
    }

    [Fact]
    public void EmulationPreventionBytesAreRemovedInACopy()
    {
        using var audit = new PoolAudit();
        var scope = new AllocationScope(ImageResourceLimits.Default, audit.Pool, nameof(EmulationPreventionBytesAreRemovedInACopy));
        using (var buffer = new HevcRbspBuffer(scope))
        {
            // The byte 3 after two zero bytes is removed: at the positions 3, 7, 10 and 14 of the payload. Two zero bytes
            // right after a removed byte, a byte 3 that is data and the last byte of the payload are covered.
            byte[] data = [0x40, 0x01, 0xAB, 0x00, 0x00, 0x03, 0x01, 0x00, 0x00, 0x03, 0x00, 0x00, 0x03, 0x03, 0x00, 0x00, 0x03];
            var copy = (byte[])data.Clone();
            buffer.Load(data, 2, data.Length - 2);
            Assert.Equal(copy, data);
            Assert.NotSame(data, buffer.Array);
            Assert.Equal(4, buffer.EmulationPreventionByteCount);
            byte[] expected = [0xAB, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x00];
            Assert.Equal(expected, buffer.Span.ToArray());
            Assert.Equal(expected, buffer.Load(data.AsSpan(2)).ToArray());

            // The positions of the payload bytes in the NAL unit, and back: the byte 0x01 is the byte 4 of the source and
            // the byte 3 of the payload; a position on a removed byte is the position of the byte that follows it
            buffer.Load(data, 2, data.Length - 2);
            int[] sourceOffsets = [0, 1, 2, 4, 5, 6, 8, 9, 11, 12, 13];
            for (var i = 0; i < sourceOffsets.Length; i++)
            {
                Assert.Equal(sourceOffsets[i], buffer.ToSourceOffset(i));
                Assert.Equal(i, buffer.FromSourceOffset(sourceOffsets[i]));
            }

            Assert.Equal(3, buffer.FromSourceOffset(3));
            Assert.Equal(6, buffer.FromSourceOffset(7));
            Assert.Equal(8, buffer.FromSourceOffset(10));
            Assert.Equal(11, buffer.FromSourceOffset(14));
            Assert.Equal(11, buffer.FromSourceOffset(15));

            // A larger payload replaces the buffers; a smaller one reuses them
            var large = new byte[5000];
            large[4999] = 3;
            buffer.Load(large, 0, large.Length);
            Assert.Equal(4999, buffer.Length);
            Assert.True(buffer.Span.IndexOfAnyExcept((byte)0) < 0);
        }

        Assert.Null(audit.Verify());
    }
}
