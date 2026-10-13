using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The HEVC decoder configuration record of an <c>hvcC</c> box (ISO/IEC 14496-15, section 8.3.3.1): the sample format of
/// the bitstream, the size of the length field that precedes each NAL unit of the coded data, and arrays of parameter sets
/// that the coded data may omit.
/// </summary>
internal static class HeicConfigurationRecord
{
    // The fields that precede the arrays: 22 bytes, then the number of arrays
    private const int HeaderLength = 23;

    /// <summary>Reads and validates a record: its sample format is returned.</summary>
    /// <param name="binding">The format of the file, which errors report.</param>
    /// <param name="reader">The payload of the <c>hvcC</c> box.</param>
    /// <exception cref="InvalidImageContentException">The record is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The record declares a version or a sample format that is not supported.</exception>
    public static HeifSampleFormat Read(HeifCodecBinding binding, ref HeifBoxReader reader)
    {
        if (reader.ReadByte() is not 1)
            throw binding.Unsupported("The version of the HEVC configuration record is not supported.", "configuration record version");

        // general_profile_space, general_tier_flag, general_profile_idc, general_profile_compatibility_flags,
        // general_constraint_indicator_flags, general_level_idc, min_spatial_segmentation_idc and parallelismType: the
        // parameter sets of the bitstream are authoritative
        reader.ReadBytes(15);
        var chromaFormat = reader.ReadByte() & 3;
        var bitDepthLuma = (reader.ReadByte() & 7) + 8;
        var bitDepthChroma = (reader.ReadByte() & 7) + 8;

        // avgFrameRate, then constantFrameRate, numTemporalLayers, temporalIdNested and lengthSizeMinusOne
        reader.ReadUInt16();
        if ((reader.ReadByte() & 3) is 2)
            throw binding.Invalid("the HEVC configuration record has a NAL unit length size that is not allowed.");

        var arrayCount = reader.ReadByte();
        for (var i = 0; i < arrayCount; i++)
        {
            reader.ReadByte();
            var unitCount = reader.ReadUInt16();
            for (var j = 0; j < unitCount; j++)
            {
                // A NAL unit has a header of two bytes
                var length = reader.ReadUInt16();
                if (length < 2)
                    throw binding.Invalid("the HEVC configuration record has a NAL unit that is not valid.");

                reader.ReadBytes(length);
            }
        }

        var isMonochrome = chromaFormat is 0;
        if (bitDepthLuma > 12 || (!isMonochrome && bitDepthChroma != bitDepthLuma))
            throw binding.Unsupported($"The bit depths of the HEIC image ({bitDepthLuma} for luma, {bitDepthChroma} for chroma) are not supported.", "bit depth");

        // chroma_format_idc: 1 is 4:2:0, 2 is 4:2:2, 3 is 4:4:4
        return new HeifSampleFormat(bitDepthLuma, isMonochrome, chromaFormat is 1 or 2 ? 1 : 0, chromaFormat is 1 ? 1 : 0);
    }

    /// <summary>
    /// Writes the <c>hvcC</c> box of a stream that the library encodes: its profile, tier and level, its sample format, and
    /// one complete array for each of its three parameter sets. The NAL units of the coded data have a length of 4 bytes.
    /// </summary>
    /// <param name="writer">The writer of the container.</param>
    /// <param name="parameters">What the parameter sets signal.</param>
    /// <param name="videoParameterSet">The NAL unit of the video parameter set, with its header.</param>
    /// <param name="sequenceParameterSet">The NAL unit of the sequence parameter set, with its header.</param>
    /// <param name="pictureParameterSet">The NAL unit of the picture parameter set, with its header.</param>
    public static void Write(HeifBoxWriter writer, in HevcSequenceParameters parameters, ReadOnlySpan<byte> videoParameterSet, ReadOnlySpan<byte> sequenceParameterSet, ReadOnlySpan<byte> pictureParameterSet)
    {
        var box = writer.BeginBox(HeifFormat.HvcC);
        writer.WriteByte(1); // configurationVersion

        // general_profile_space (0), general_tier_flag, general_profile_idc
        writer.WriteByte((byte)((parameters.GeneralTierFlag ? 0x20 : 0) | parameters.GeneralProfileIdc));
        writer.WriteUInt32(parameters.GeneralProfileCompatibilityFlags);

        // The 48 bits of general_constraint_indicator_flags
        var constraints = parameters.GeneralConstraintIndicatorFlags;
        writer.WriteUInt16((int)(constraints >> 32));
        writer.WriteUInt32((uint)constraints);
        writer.WriteByte((byte)parameters.GeneralLevelIdc);

        // min_spatial_segmentation_idc (0: not signalled) and parallelismType (0: unknown), after their reserved bits
        writer.WriteUInt16(0xF000);
        writer.WriteByte(0xFC);
        writer.WriteByte((byte)(0xFC | parameters.ChromaFormatIdc));
        writer.WriteByte((byte)(0xF8 | (parameters.BitDepth - 8)));
        writer.WriteByte((byte)(0xF8 | (parameters.BitDepth - 8)));

        // avgFrameRate (0: unspecified); constantFrameRate (0), numTemporalLayers (1), temporalIdNested (1) and
        // lengthSizeMinusOne (3)
        writer.WriteUInt16(0);
        writer.WriteByte(0x0F);
        writer.WriteByte(3); // numOfArrays
        WriteArray(writer, HevcConstants.NalVps, videoParameterSet);
        WriteArray(writer, HevcConstants.NalSps, sequenceParameterSet);
        WriteArray(writer, HevcConstants.NalPps, pictureParameterSet);
        writer.EndBox(box);

        static void WriteArray(HeifBoxWriter writer, int nalUnitType, ReadOnlySpan<byte> nalUnit)
        {
            // array_completeness: the coded data hold no NAL unit of this type
            writer.WriteByte((byte)(0x80 | nalUnitType));
            writer.WriteUInt16(1);
            writer.WriteUInt16(nalUnit.Length);
            writer.WriteBytes(nalUnit);
        }
    }

    /// <summary>Gets the number of bytes of the length field that precedes each NAL unit of the coded data: 1, 2 or 4.</summary>
    /// <param name="record">A record that <see cref="Read"/> validated.</param>
    public static int GetNalUnitLengthSize(ReadOnlySpan<byte> record) => (record[21] & 3) + 1;

    /// <summary>Enumerates the NAL units of the arrays of a record, in order.</summary>
    /// <param name="record">A record that <see cref="Read"/> validated.</param>
    /// <param name="position">The position of the enumeration: 0 to start, updated by each call.</param>
    /// <param name="nalUnit">The next NAL unit, with its header.</param>
    /// <returns><see langword="false"/> after the last NAL unit.</returns>
    public static bool TryReadNalUnit(ReadOnlySpan<byte> record, scoped ref Position position, out ReadOnlySpan<byte> nalUnit)
    {
        if (position.Offset is 0)
        {
            position.Offset = HeaderLength;
            position.RemainingArrays = record[HeaderLength - 1];
        }

        while (position.RemainingUnits is 0)
        {
            if (position.RemainingArrays is 0)
            {
                nalUnit = default;
                return false;
            }

            position.RemainingArrays--;
            position.RemainingUnits = BinaryPrimitives.ReadUInt16BigEndian(record[(position.Offset + 1)..]);
            position.Offset += 3;
        }

        var length = BinaryPrimitives.ReadUInt16BigEndian(record[position.Offset..]);
        nalUnit = record.Slice(position.Offset + 2, length);
        position.Offset += 2 + length;
        position.RemainingUnits--;
        return true;
    }

    /// <summary>The position of an enumeration of the NAL units of a record.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal struct Position
    {
        public int Offset { get; set; }

        public int RemainingArrays { get; set; }

        public int RemainingUnits { get; set; }
    }
}
