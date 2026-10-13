using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.TestHarness.Hevc;

/// <summary>
/// An independent reader of the structure of an HEVC bitstream (Rec. ITU-T H.265): the NAL units of a byte stream (Annex B),
/// their grouping into access units, the length-prefixed form that files store (ISO/IEC 14496-15), and the fields of a
/// sequence parameter set that describe the sample arrays. It shares no code with the library.
/// </summary>
public static class ReferenceHevcStream
{
    /// <summary>The NAL unit type of a video parameter set.</summary>
    public const int NalVps = 32;

    /// <summary>The NAL unit type of a sequence parameter set.</summary>
    public const int NalSps = 33;

    /// <summary>The NAL unit type of a picture parameter set.</summary>
    public const int NalPps = 34;

    /// <summary>The NAL unit type of a suffix SEI NAL unit.</summary>
    public const int NalSuffixSei = 40;

    /// <summary>Gets <c>nal_unit_type</c> of a NAL unit (clause 7.3.1.2).</summary>
    public static int GetNalUnitType(ReadOnlySpan<byte> nalUnit) => (nalUnit[0] >> 1) & 0x3F;

    /// <summary>Gets <c>nuh_layer_id</c> of a NAL unit.</summary>
    public static int GetLayerId(ReadOnlySpan<byte> nalUnit) => ((nalUnit[0] & 1) << 5) | (nalUnit[1] >> 3);

    /// <summary>
    /// Splits a byte stream (clause B.2) into its NAL units: the bytes between the start code prefixes, without the zero
    /// bytes that precede a prefix.
    /// </summary>
    /// <param name="stream">The byte stream.</param>
    /// <returns>The NAL units, header included, in order.</returns>
    /// <exception cref="InvalidDataException">The stream does not start with a start code prefix.</exception>
    public static IReadOnlyList<ReadOnlyMemory<byte>> SplitAnnexB(ReadOnlyMemory<byte> stream)
    {
        var data = stream.Span;
        var units = new List<ReadOnlyMemory<byte>>();
        var position = FindStartCode(data, 0);
        if (position < 0)
            throw new InvalidDataException("The byte stream has no start code prefix.");

        if (data[..position].ContainsAnyExcept((byte)0))
            throw new InvalidDataException("The byte stream does not start with a start code prefix.");

        while (position >= 0)
        {
            var start = position + 3;
            var next = FindStartCode(data, start);
            var end = next < 0 ? data.Length : next;

            // trailing_zero_8bits, and the zero_byte of the next start code
            while (end > start && data[end - 1] is 0)
            {
                end--;
            }

            if (end - start < 2)
                throw new InvalidDataException("A NAL unit of the byte stream is shorter than its header.");

            units.Add(stream[start..end]);
            position = next;
        }

        return units;
    }

    /// <summary>
    /// Groups the NAL units of a bitstream into access units (clause 7.4.2.4.4): an access unit starts with the first NAL
    /// unit, after the last slice segment of a picture, that can only precede the slice segments of the next one.
    /// </summary>
    /// <param name="nalUnits">The NAL units of the base layer, in decoding order.</param>
    /// <returns>The NAL units of each access unit.</returns>
    public static IReadOnlyList<IReadOnlyList<ReadOnlyMemory<byte>>> GroupAccessUnits(IReadOnlyList<ReadOnlyMemory<byte>> nalUnits)
    {
        ArgumentNullException.ThrowIfNull(nalUnits);
        var accessUnits = new List<IReadOnlyList<ReadOnlyMemory<byte>>>();
        var current = new List<ReadOnlyMemory<byte>>();
        var hasSlice = false;
        foreach (var unit in nalUnits)
        {
            var span = unit.Span;
            var type = GetNalUnitType(span);
            var isSlice = type <= 31;

            // first_slice_segment_in_pic_flag is the first bit of a slice segment header
            var startsPicture = isSlice && (span[2] & 0x80) is not 0;
            var precedesPicture = type is (>= 32 and <= 35) or 39 or (>= 41 and <= 44) or (>= 48 and <= 55);
            if (hasSlice && (startsPicture || precedesPicture))
            {
                accessUnits.Add(current);
                current = [];
                hasSlice = false;
            }

            current.Add(unit);
            hasSlice |= isSlice;
        }

        if (current.Count > 0)
        {
            accessUnits.Add(current);
        }

        return accessUnits;
    }

    /// <summary>Writes NAL units one after the other, each one preceded by its length in <paramref name="nalLengthSize"/> bytes.</summary>
    /// <param name="nalUnits">The NAL units.</param>
    /// <param name="nalLengthSize">The number of bytes of a length: 1, 2 or 4.</param>
    /// <returns>The length-prefixed NAL units.</returns>
    public static byte[] ToLengthPrefixed(IEnumerable<ReadOnlyMemory<byte>> nalUnits, int nalLengthSize = 4)
    {
        ArgumentNullException.ThrowIfNull(nalUnits);
        if (nalLengthSize is not (1 or 2 or 4))
            throw new ArgumentOutOfRangeException(nameof(nalLengthSize));

        using var output = new MemoryStream();
        Span<byte> length = stackalloc byte[4];
        foreach (var unit in nalUnits)
        {
            if (nalLengthSize < 4 && unit.Length >= 1 << (8 * nalLengthSize))
                throw new ArgumentException("A NAL unit is longer than the largest length.", nameof(nalUnits));

            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)unit.Length);
            output.Write(length[(4 - nalLengthSize)..]);
            output.Write(unit.Span);
        }

        return output.ToArray();
    }

    /// <summary>Splits length-prefixed NAL units.</summary>
    /// <param name="data">The NAL units, each one preceded by its length.</param>
    /// <param name="nalLengthSize">The number of bytes of a length: 1, 2 or 4.</param>
    /// <returns>The NAL units, header included, in order.</returns>
    /// <exception cref="InvalidDataException">A length goes beyond the end of the data.</exception>
    public static IReadOnlyList<ReadOnlyMemory<byte>> SplitLengthPrefixed(ReadOnlyMemory<byte> data, int nalLengthSize = 4)
    {
        var units = new List<ReadOnlyMemory<byte>>();
        var position = 0;
        while (position < data.Length)
        {
            if (data.Length - position < nalLengthSize)
                throw new InvalidDataException("The length of a NAL unit is truncated.");

            var length = 0;
            for (var i = 0; i < nalLengthSize; i++)
            {
                length = (length << 8) | data.Span[position + i];
            }

            position += nalLengthSize;
            if (length < 0 || length > data.Length - position)
                throw new InvalidDataException("A NAL unit goes beyond the end of the data.");

            units.Add(data.Slice(position, length));
            position += length;
        }

        return units;
    }

    /// <summary>Writes NAL units as a byte stream: each one preceded by a four-byte start code.</summary>
    /// <param name="nalUnits">The NAL units.</param>
    /// <returns>The byte stream.</returns>
    public static byte[] ToAnnexB(IEnumerable<ReadOnlyMemory<byte>> nalUnits)
    {
        ArgumentNullException.ThrowIfNull(nalUnits);
        using var output = new MemoryStream();
        foreach (var unit in nalUnits)
        {
            output.Write([0, 0, 0, 1]);
            output.Write(unit.Span);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Returns the payload of a NAL unit: the bytes that follow its two-byte header, without the emulation prevention bytes
    /// (clause 7.3.1.1).
    /// </summary>
    /// <param name="nalUnit">The NAL unit.</param>
    /// <returns>The raw byte sequence payload.</returns>
    public static byte[] GetRbsp(ReadOnlySpan<byte> nalUnit)
    {
        var rbsp = new List<byte>(nalUnit.Length);
        var zeros = 0;
        for (var i = 2; i < nalUnit.Length; i++)
        {
            var value = nalUnit[i];
            if (zeros >= 2 && value is 3)
            {
                zeros = 0;
                continue;
            }

            zeros = value is 0 ? zeros + 1 : 0;
            rbsp.Add(value);
        }

        return [.. rbsp];
    }

    /// <summary>Reads the fields of a sequence parameter set that describe the sample arrays of the pictures (clause 7.3.2.2.1).</summary>
    /// <param name="nalUnit">The NAL unit of the sequence parameter set.</param>
    /// <returns>The description.</returns>
    /// <exception cref="InvalidDataException">The NAL unit is not a sequence parameter set.</exception>
    public static ReferenceHevcSequence ReadSequenceParameterSet(ReadOnlySpan<byte> nalUnit)
    {
        if (GetNalUnitType(nalUnit) is not NalSps)
            throw new InvalidDataException("The NAL unit is not a sequence parameter set.");

        var bits = new BitCursor(GetRbsp(nalUnit));

        // sps_video_parameter_set_id
        bits.Read(4);
        var maxSubLayersMinus1 = (int)bits.Read(3);

        // sps_temporal_id_nesting_flag, then profile_tier_level( 1, sps_max_sub_layers_minus1 ): 88 bits of general
        // profile and 8 bits of general level
        bits.Read(1);
        bits.Skip(88 + 8);
        var subLayerProfilePresent = new bool[8];
        var subLayerLevelPresent = new bool[8];
        for (var i = 0; i < maxSubLayersMinus1; i++)
        {
            subLayerProfilePresent[i] = bits.Read(1) is not 0;
            subLayerLevelPresent[i] = bits.Read(1) is not 0;
        }

        if (maxSubLayersMinus1 > 0)
        {
            bits.Skip(2 * (8 - maxSubLayersMinus1));
        }

        for (var i = 0; i < maxSubLayersMinus1; i++)
        {
            bits.Skip((subLayerProfilePresent[i] ? 88 : 0) + (subLayerLevelPresent[i] ? 8 : 0));
        }

        // sps_seq_parameter_set_id
        bits.ReadUE();
        var chromaFormatIdc = (int)bits.ReadUE();
        if (chromaFormatIdc is 3)
        {
            // separate_colour_plane_flag
            bits.Read(1);
        }

        var width = (int)bits.ReadUE();
        var height = (int)bits.ReadUE();
        var left = 0;
        var right = 0;
        var top = 0;
        var bottom = 0;
        if (bits.Read(1) is not 0)
        {
            left = (int)bits.ReadUE();
            right = (int)bits.ReadUE();
            top = (int)bits.ReadUE();
            bottom = (int)bits.ReadUE();
        }

        var bitDepthLuma = (int)bits.ReadUE() + 8;
        var bitDepthChroma = (int)bits.ReadUE() + 8;

        // Table 6-1: SubWidthC and SubHeightC; the offsets of the conformance cropping window are in chroma sample units
        var subWidthC = chromaFormatIdc is 1 or 2 ? 2 : 1;
        var subHeightC = chromaFormatIdc is 1 ? 2 : 1;
        return new ReferenceHevcSequence(width, height, chromaFormatIdc, bitDepthLuma, bitDepthChroma, subWidthC * left, subHeightC * top, width - (subWidthC * (left + right)), height - (subHeightC * (top + bottom)));
    }

    private static int FindStartCode(ReadOnlySpan<byte> data, int from)
    {
        var index = data[from..].IndexOf((ReadOnlySpan<byte>)[0, 0, 1]);
        return index < 0 ? -1 : from + index;
    }

    private ref struct BitCursor(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position;

        public uint Read(int count)
        {
            uint value = 0;
            for (var i = 0; i < count; i++)
            {
                if (_position >= _data.Length * 8)
                    throw new InvalidDataException("The sequence parameter set is truncated.");

                value = (value << 1) | (uint)((_data[_position >> 3] >> (7 - (_position & 7))) & 1);
                _position++;
            }

            return value;
        }

        public void Skip(int count) => _position += count;

        // Clause 9.2: 0-th order Exp-Golomb code
        public uint ReadUE()
        {
            var leadingZeroBits = 0;
            while (Read(1) is 0)
            {
                if (++leadingZeroBits > 31)
                    throw new InvalidDataException("An Exp-Golomb code of the sequence parameter set is too long.");
            }

            return (1u << leadingZeroBits) - 1 + Read(leadingZeroBits);
        }
    }
}
