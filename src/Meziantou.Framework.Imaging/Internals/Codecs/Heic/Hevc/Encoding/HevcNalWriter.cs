using System.Buffers.Binary;
using System.Diagnostics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes NAL units (Rec. ITU-T H.265, clauses 7.3.1.1 "General NAL unit syntax" and 7.3.1.2 "NAL unit header syntax"): the
/// two bytes of the header, then the raw byte sequence payload with the emulation prevention bytes that keep a start code
/// from appearing in it (clause 7.4.2.3 "Encapsulation of an SODB within an RBSP"). The mirror of
/// <see cref="HevcNalUnitHeader"/> and of <see cref="HevcRbspBuffer"/>.
/// </summary>
/// <remarks>
/// The specification does not say how NAL units follow each other. The items and the samples of a HEIF file precede each
/// NAL unit with its length (ISO/IEC 14496-15, <c>lengthSizeMinusOne</c>), a raw stream precedes it with a start code
/// (Annex B), and a decoder configuration record stores the NAL units of the parameter sets alone.
/// </remarks>
internal static class HevcNalWriter
{
    /// <summary>The number of bytes of the length that <see cref="WriteLengthPrefixed"/> writes before a NAL unit.</summary>
    public const int NalLengthSize = 4;

    /// <summary>
    /// Appends a NAL unit of the base layer to <paramref name="output"/>, without anything before it.
    /// </summary>
    /// <param name="output">The destination. It is not the buffer that holds <paramref name="rbsp"/>.</param>
    /// <param name="nalUnitType"><c>nal_unit_type</c> (Table 7-1).</param>
    /// <param name="temporalId"><c>TemporalId</c>: <c>nuh_temporal_id_plus1 - 1</c>.</param>
    /// <param name="rbsp">The payload, with its trailing bits and without emulation prevention bytes.</param>
    /// <returns>The number of bytes of the NAL unit: <c>NumBytesInNalUnit</c>.</returns>
    public static int WriteNalUnit(PooledPayloadWriter output, int nalUnitType, int temporalId, ReadOnlySpan<byte> rbsp)
    {
        ArgumentNullException.ThrowIfNull(output);
        Debug.Assert(nalUnitType is >= 0 and <= 63);
        Debug.Assert(temporalId is >= 0 and <= 6);
        Debug.Assert(!output.WrittenSpan.Overlaps(rbsp));
        var start = output.Length;

        // forbidden_zero_bit, nal_unit_type, nuh_layer_id equal to 0, nuh_temporal_id_plus1
        output.Write((byte)(nalUnitType << 1));
        output.Write((byte)(temporalId + 1));
        WriteEscaped(output, rbsp);
        return output.Length - start;
    }

    /// <summary>
    /// Appends a NAL unit preceded by its length in <see cref="NalLengthSize"/> bytes, most significant byte first: the
    /// format of the items and of the samples of a HEIF file.
    /// </summary>
    /// <returns>The number of bytes of the NAL unit, without its length.</returns>
    public static int WriteLengthPrefixed(PooledPayloadWriter output, int nalUnitType, int temporalId, ReadOnlySpan<byte> rbsp)
    {
        ArgumentNullException.ThrowIfNull(output);
        var start = output.Length;
        output.WriteZeros(NalLengthSize);
        var length = WriteNalUnit(output, nalUnitType, temporalId, rbsp);
        BinaryPrimitives.WriteUInt32BigEndian(output.WrittenSpan.Slice(start, NalLengthSize), (uint)length);
        return length;
    }

    /// <summary>
    /// Appends a NAL unit in the byte stream format (Annex B, clause B.2.1 "Byte stream NAL unit syntax"): a
    /// <c>zero_byte</c> and the start code prefix <c>0x000001</c>, then the NAL unit. The <c>zero_byte</c> is required
    /// before a parameter set and before the first NAL unit of an access unit, and allowed before any NAL unit.
    /// </summary>
    /// <returns>The number of bytes of the NAL unit, without its start code.</returns>
    public static int WriteByteStream(PooledPayloadWriter output, int nalUnitType, int temporalId, ReadOnlySpan<byte> rbsp)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Write([0, 0, 0, 1]);
        return WriteNalUnit(output, nalUnitType, temporalId, rbsp);
    }

    // Clause 7.4.2.3: a byte equal to 3 is inserted before a byte of the payload that is at most 3 and follows two bytes
    // equal to 0. The byte that follows an inserted byte starts a new search: the two zero bytes before it do not count
    // anymore, and it counts itself when it is zero. A byte equal to 3 is also appended to a payload that ends with a
    // cabac_zero_word, the only payloads whose last byte is zero: a NAL unit does not end with a zero byte. The appended
    // byte follows two zero bytes that no inserted byte separates, so that a decoder removes it.
    private static void WriteEscaped(PooledPayloadWriter output, ReadOnlySpan<byte> rbsp)
    {
        var start = 0;
        var position = 0;
        while (true)
        {
            // The next pair of zero bytes that a third byte follows
            var index = rbsp[position..].IndexOf((ReadOnlySpan<byte>)[0, 0]);
            if (index < 0 || position + index + 2 >= rbsp.Length)
                break;

            position += index + 2;
            if (rbsp[position] <= 3)
            {
                output.Write(rbsp[start..position]);
                output.Write(3);
                start = position;
            }
        }

        var remaining = rbsp[start..];
        output.Write(remaining);
        if (remaining.EndsWith((ReadOnlySpan<byte>)[0, 0]))
        {
            output.Write(3);
        }
    }
}
