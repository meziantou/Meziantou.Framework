using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Benchmarks.Workloads;

/// <summary>
/// A minimal HEIC file around an HEVC stream of the golden corpus (a still image for one access unit, an image sequence
/// otherwise), so that the decoding workloads measure streams that another encoder wrote. The parameter sets go to the
/// configuration record and the other NAL units, preceded by their length, to the data of the item or of the samples.
/// </summary>
internal static class HevcStreamContainer
{
    private const int NalVps = 32;
    private const int NalSps = 33;
    private const int NalPps = 34;

    /// <summary>Wraps a byte stream (ITU-T H.265, annex B) into an HEIC file.</summary>
    /// <param name="stream">The stream: NAL units preceded by start codes.</param>
    /// <param name="width">The width of the pictures that a decoder outputs.</param>
    /// <param name="height">The height of the pictures that a decoder outputs.</param>
    /// <param name="bitDepth">The bit depth: 8, 10 or 12.</param>
    /// <param name="chromaFormatIdc">The chroma format: 0 for 4:0:0, 1 for 4:2:0, 2 for 4:2:2, 3 for 4:4:4.</param>
    public static byte[] Wrap(ReadOnlySpan<byte> stream, int width, int height, int bitDepth, int chromaFormatIdc)
    {
        var parameterSets = new List<byte[]>();
        var samples = new List<byte[]>();
        var current = new List<byte>();
        var hasPicture = false;
        foreach (var (offset, length) in SplitNalUnits(stream))
        {
            var nalUnit = stream.Slice(offset, length);
            var type = (nalUnit[0] >> 1) & 0x3F;
            if (type is NalVps or NalSps or NalPps)
            {
                // The parameter sets of the streams of the corpus are not repeated with other values
                var parameterSet = nalUnit.ToArray();
                if (!parameterSets.Exists(known => known.AsSpan().SequenceEqual(parameterSet)))
                    parameterSets.Add(parameterSet);

                continue;
            }

            // ITU-T H.265, clause 7.4.2.4.4: after the last slice segment of a picture, an access unit delimiter, a prefix
            // SEI message or a NAL unit of the types reserved for what precedes a picture starts the next access unit, and
            // so does the first slice segment of the next picture (first_slice_segment_in_pic_flag follows the header of
            // two bytes); a suffix SEI message, an end of sequence and filler data belong to the picture they follow
            var isSlice = type < 32;
            var startsAccessUnit = isSlice ? (nalUnit[2] & 0x80) is not 0 : type is 35 or 39 or (>= 41 and <= 44) or (>= 48 and <= 55);
            if (hasPicture && startsAccessUnit)
            {
                samples.Add([.. current]);
                current.Clear();
                hasPicture = false;
            }

            current.AddRange(UInt32(length));
            current.AddRange(nalUnit);
            hasPicture |= isSlice;
        }

        if (current.Count > 0)
            samples.Add([.. current]);

        if (samples.Count is 0 || parameterSets.Count < 3)
            throw new InvalidOperationException("The HEVC stream has no picture or no parameter set.");

        var configuration = WriteConfiguration(parameterSets, bitDepth, chromaFormatIdc);
        return samples.Count is 1 ? WrapStill(samples[0], width, height, configuration) : WrapSequence(samples, width, height, configuration);
    }

    // ISO/IEC 14496-15, section 8.3.3.1: the record of an 'hvcC' box. The profile, the tier and the level are left at 0:
    // the parameter sets say them
    private static byte[] WriteConfiguration(List<byte[]> parameterSets, int bitDepth, int chromaFormatIdc)
    {
        var record = new List<byte> { 1 };
        record.AddRange(new byte[12]);
        record.AddRange([0xF0, 0x00, 0xFC, (byte)(0xFC | chromaFormatIdc), (byte)(0xF8 | (bitDepth - 8)), (byte)(0xF8 | (bitDepth - 8)), 0, 0, 0x0F]);
        ReadOnlySpan<int> types = [NalVps, NalSps, NalPps];
        record.Add((byte)types.Length);
        foreach (var type in types)
        {
            var units = parameterSets.FindAll(parameterSet => ((parameterSet[0] >> 1) & 0x3F) == type);
            record.Add((byte)(0x80 | type));
            record.AddRange(UInt16(units.Count));
            foreach (var unit in units)
            {
                record.AddRange(UInt16(unit.Length));
                record.AddRange(unit);
            }
        }

        return WriteBox("hvcC", [.. record]);
    }

    // The NAL units of a byte stream: what is between two start codes (three bytes 0, 0, 1), without the zero bytes that
    // precede the next start code
    private static List<(int Offset, int Length)> SplitNalUnits(ReadOnlySpan<byte> stream)
    {
        var units = new List<(int Offset, int Length)>();
        var start = -1;
        var offset = 0;
        while (true)
        {
            var next = stream[offset..].IndexOf((ReadOnlySpan<byte>)[0, 0, 1]);
            var end = next < 0 ? stream.Length : offset + next;
            if (start >= 0)
            {
                var length = end - start;
                while (length > 0 && stream[start + length - 1] is 0)
                {
                    length--;
                }

                if (length > 2)
                    units.Add((start, length));
            }

            if (next < 0)
                return units;

            start = end + 3;
            offset = start;
        }
    }

    // ISO/IEC 23008-12: a primary item of type 'hvc1' with its size and its configuration
    private static byte[] WrapStill(byte[] data, int width, int height, byte[] configuration)
    {
        var fileType = WriteBox("ftyp", [.. "heix"u8, 0, 0, 0, 0, .. "heix"u8, .. "mif1"u8, .. "miaf"u8]);
        var handler = WriteFullBox("hdlr", 0, 0, [0, 0, 0, 0, .. "pict"u8, .. new byte[12], 0]);
        var primaryItem = WriteFullBox("pitm", 0, 0, [0, 1]);
        var itemInformation = WriteFullBox("iinf", 0, 0, [0, 1, .. WriteFullBox("infe", 2, 0, [0, 1, 0, 0, .. "hvc1"u8, 0])]);
        var size = WriteFullBox("ispe", 0, 0, [.. UInt32(width), .. UInt32(height)]);
        var properties = WriteBox("iprp", [.. WriteBox("ipco", [.. size, .. configuration]), .. WriteFullBox("ipma", 0, 0, [0, 0, 0, 1, 0, 1, 2, 0x01, 0x82])]);

        // The offset of the item is the offset of the payload of the 'mdat' box, which follows the 'meta' box
        byte[] BuildMeta(int dataOffset)
        {
            var location = WriteFullBox("iloc", 0, 0, [0x44, 0x00, 0, 1, 0, 1, 0, 0, 0, 1, .. UInt32(dataOffset), .. UInt32(data.Length)]);
            return WriteFullBox("meta", 0, 0, [.. handler, .. primaryItem, .. location, .. itemInformation, .. properties]);
        }

        var meta = BuildMeta(fileType.Length + BuildMeta(0).Length + 8);
        return [.. fileType, .. meta, .. WriteBox("mdat", data)];
    }

    // ISO/IEC 14496-12: one 'pict' track whose samples are the access units, in decoding order and in one chunk. The track
    // has no composition offsets: the decoder outputs its pictures in their order, each for the same duration
    private static byte[] WrapSequence(List<byte[]> samples, int width, int height, byte[] configuration)
    {
        var fileType = WriteBox("ftyp", [.. "hevx"u8, 0, 0, 0, 0, .. "hevx"u8, .. "msf1"u8, .. "iso8"u8, .. "miaf"u8]);
        var count = samples.Count;
        byte[] BuildMovie(int dataOffset)
        {
            byte[] sampleEntry =
            [
                0, 0, 0, 0, 0, 0, 0, 1, .. new byte[16], .. UInt16(width), .. UInt16(height), 0, 0x48, 0, 0, 0, 0x48, 0, 0, 0, 0, 0, 0, 0, 1, .. new byte[32], 0, 0x18, 0xFF, 0xFF,
                .. configuration,
            ];
            var sizes = new List<byte>();
            foreach (var sample in samples)
            {
                sizes.AddRange(UInt32(sample.Length));
            }

            var sampleTable = WriteBox("stbl",
            [
                .. WriteFullBox("stsd", 0, 0, [0, 0, 0, 1, .. WriteBox("hvc1", sampleEntry)]),
                .. WriteFullBox("stts", 0, 0, [0, 0, 0, 1, .. UInt32(count), 0, 0, 0, 1]),
                .. WriteFullBox("stsc", 0, 0, [0, 0, 0, 1, 0, 0, 0, 1, .. UInt32(count), 0, 0, 0, 1]),
                .. WriteFullBox("stsz", 0, 0, [0, 0, 0, 0, .. UInt32(count), .. sizes]),
                .. WriteFullBox("stco", 0, 0, [0, 0, 0, 1, .. UInt32(dataOffset)]),
            ]);
            var media = WriteBox("mdia",
            [
                .. WriteFullBox("mdhd", 0, 0, [.. new byte[8], 0, 0, 0, 10, .. UInt32(count), 0x55, 0xC4, 0, 0]),
                .. WriteFullBox("hdlr", 0, 0, [0, 0, 0, 0, .. "pict"u8, .. new byte[12], 0]),
                .. WriteBox("minf", [.. WriteFullBox("vmhd", 0, 1, new byte[8]), .. sampleTable]),
            ]);
            var trackHeader = WriteFullBox("tkhd", 0, 3,
            [
                .. new byte[8], 0, 0, 0, 1, 0, 0, 0, 0, .. UInt32(count), .. new byte[16],
                0, 1, 0, 0, .. new byte[12], 0, 1, 0, 0, .. new byte[12], 0x40, 0, 0, 0,
                .. UInt16(width), 0, 0, .. UInt16(height), 0, 0,
            ]);
            var movieHeader = WriteFullBox("mvhd", 0, 0,
            [
                .. new byte[8], 0, 0, 0, 10, .. UInt32(count), 0, 1, 0, 0, 1, 0, .. new byte[10],
                0, 1, 0, 0, .. new byte[12], 0, 1, 0, 0, .. new byte[12], 0x40, 0, 0, 0,
                .. new byte[24], 0, 0, 0, 2,
            ]);
            return WriteBox("moov", [.. movieHeader, .. WriteBox("trak", [.. trackHeader, .. media])]);
        }

        var movie = BuildMovie(fileType.Length + BuildMovie(0).Length + 8);
        var data = new List<byte>();
        foreach (var sample in samples)
        {
            data.AddRange(sample);
        }

        return [.. fileType, .. movie, .. WriteBox("mdat", [.. data])];
    }

    private static byte[] WriteBox(string type, ReadOnlySpan<byte> payload)
    {
        var box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        System.Text.Encoding.ASCII.GetBytes(type, box.AsSpan(4, 4));
        payload.CopyTo(box.AsSpan(8));
        return box;
    }

    private static byte[] WriteFullBox(string type, byte version, int flags, ReadOnlySpan<byte> payload)
        => WriteBox(type, [version, (byte)(flags >> 16), (byte)(flags >> 8), (byte)flags, .. payload]);

    private static byte[] UInt32(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static byte[] UInt16(int value) => [(byte)(value >> 8), (byte)value];
}
