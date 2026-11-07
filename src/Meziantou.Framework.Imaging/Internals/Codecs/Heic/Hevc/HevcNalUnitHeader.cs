using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The header of a NAL unit (Rec. ITU-T H.265, clause 7.3.1.2 "NAL unit header syntax").</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct HevcNalUnitHeader
{
    /// <summary>The number of bytes of the header.</summary>
    public const int Length = 2;

    private HevcNalUnitHeader(int nalUnitType, int nuhLayerId, int temporalId)
    {
        NalUnitType = nalUnitType;
        NuhLayerId = nuhLayerId;
        TemporalId = temporalId;
    }

    /// <summary>Gets <c>nal_unit_type</c> (Table 7-1).</summary>
    public int NalUnitType { get; }

    /// <summary>Gets <c>nuh_layer_id</c>: 0 for the base layer.</summary>
    public int NuhLayerId { get; }

    /// <summary>Gets <c>TemporalId</c>: <c>nuh_temporal_id_plus1 - 1</c>.</summary>
    public int TemporalId { get; }

    /// <summary>Gets whether the NAL unit is a video coding layer NAL unit.</summary>
    public bool IsVcl => NalUnitType <= HevcConstants.NalRsvVcl31;

    /// <summary>Gets whether the NAL unit belongs to an intra random access point picture.</summary>
    public bool IsIrap => NalUnitType is >= HevcConstants.NalBlaWLp and <= HevcConstants.NalRsvIrapVcl23;

    /// <summary>Gets whether the NAL unit belongs to an instantaneous decoding refresh picture.</summary>
    public bool IsIdr => NalUnitType is HevcConstants.NalIdrWRadl or HevcConstants.NalIdrNLp;

    /// <summary>Gets whether the NAL unit belongs to a random access skipped leading picture.</summary>
    public bool IsRasl => NalUnitType is HevcConstants.NalRaslN or HevcConstants.NalRaslR;

    /// <summary>Gets whether the NAL unit belongs to a random access decodable leading picture.</summary>
    public bool IsRadl => NalUnitType is HevcConstants.NalRadlN or HevcConstants.NalRadlR;

    /// <summary>
    /// Gets whether the NAL unit belongs to a sub-layer non-reference picture (clause 3: the odd types up to
    /// <c>RSV_VCL_N14</c> are the reference pictures of their sub-layer).
    /// </summary>
    public bool IsSubLayerNonReference => NalUnitType <= HevcConstants.NalRsvVclR15 && (NalUnitType & 1) is 0;

    /// <summary>Reads the header at the start of <paramref name="nalUnit"/>.</summary>
    /// <exception cref="InvalidImageContentException">The NAL unit is shorter than its header, or the header has a forbidden value.</exception>
    public static HevcNalUnitHeader Read(ReadOnlySpan<byte> nalUnit)
    {
        if (nalUnit.Length < Length)
            throw HevcErrors.Truncated();

        var header = BinaryPrimitives.ReadUInt16BigEndian(nalUnit);
        if ((header & 0x8000) is not 0)
            throw HevcErrors.Invalid("forbidden_zero_bit is not zero.");

        var temporalIdPlus1 = header & 7;
        if (temporalIdPlus1 is 0)
            throw HevcErrors.Invalid("nuh_temporal_id_plus1 is zero.");

        return new HevcNalUnitHeader((header >> 9) & 0x3F, (header >> 3) & 0x3F, temporalIdPlus1 - 1);
    }

    /// <summary>
    /// Reads the next NAL unit of an access unit in which every NAL unit is preceded by its length in
    /// <paramref name="nalLengthSize"/> bytes, most significant byte first (ISO/IEC 14496-15, <c>lengthSizeMinusOne</c>).
    /// </summary>
    /// <param name="data">The access unit.</param>
    /// <param name="nalLengthSize">The number of bytes of a length: 1, 2 or 4.</param>
    /// <param name="position">The position of the length of the NAL unit; it is moved after the NAL unit.</param>
    /// <param name="offset">The position of the header of the NAL unit.</param>
    /// <param name="length">The number of bytes of the NAL unit, header included.</param>
    /// <returns><see langword="false"/> at the end of the access unit.</returns>
    /// <exception cref="InvalidImageContentException">A length or a NAL unit goes beyond the end of the data, or a NAL unit is shorter than its header.</exception>
    public static bool TryReadNext(ReadOnlySpan<byte> data, int nalLengthSize, ref int position, out int offset, out int length)
    {
        offset = 0;
        length = 0;
        if (position >= data.Length)
            return false;

        if (data.Length - position < nalLengthSize)
            throw HevcErrors.Truncated();

        uint value = 0;
        for (var i = 0; i < nalLengthSize; i++)
        {
            value = (value << 8) | data[position + i];
        }

        var start = position + nalLengthSize;
        if (value > (uint)(data.Length - start))
            throw HevcErrors.Truncated();

        if (value < Length)
            throw HevcErrors.Invalid("a NAL unit is shorter than its header.");

        offset = start;
        length = (int)value;
        position = start + length;
        return true;
    }
}
