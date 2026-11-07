using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Benchmarks.Workloads;

/// <summary>
/// A minimal HEIC file around an HEVC stream of the golden corpus (a still image for one access unit, an image sequence
/// otherwise, or a grid of copies of a still image), so that the decoding workloads measure streams that another encoder wrote. The parameter sets go to the
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
        var (parameterSets, samples) = Split(stream);
        var configuration = WriteConfiguration(parameterSets, bitDepth, chromaFormatIdc);
        return samples.Count is 1 ? WrapStill(samples[0], width, height, configuration) : WrapSequence(samples, width, height, configuration);
    }

    // The parameter sets of a byte stream, and its access units as NAL units preceded by their length
    private static (List<byte[]> ParameterSets, List<byte[]> Samples) Split(ReadOnlySpan<byte> stream)
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

        return (parameterSets, samples);
    }

    /// <summary>
    /// Wraps a still image (ITU-T H.265, annex B) into an HEIC file whose primary item is a grid of copies of it, as the
    /// cameras of phones write their photos: each tile is a coded item with its own copy of the data.
    /// </summary>
    /// <param name="stream">The stream of one picture: NAL units preceded by start codes.</param>
    /// <param name="tileWidth">The width of the picture that a decoder outputs.</param>
    /// <param name="tileHeight">The height of the picture that a decoder outputs.</param>
    /// <param name="chromaFormatIdc">The chroma format: 0 for 4:0:0, 1 for 4:2:0, 2 for 4:2:2, 3 for 4:4:4.</param>
    /// <param name="columns">The number of tiles of a row of the grid.</param>
    /// <param name="rows">The number of rows of tiles.</param>
    /// <param name="width">The width of the image, which the tiles of the last column cover.</param>
    /// <param name="height">The height of the image, which the tiles of the last row cover.</param>
    public static byte[] WrapGrid(ReadOnlySpan<byte> stream, int tileWidth, int tileHeight, int chromaFormatIdc, int columns, int rows, int width, int height)
    {
        var (parameterSets, samples) = Split(stream);
        if (samples.Count is not 1)
            throw new InvalidOperationException("The HEVC stream of a tile has several pictures.");

        var configuration = WriteConfiguration(parameterSets, bitDepth: 8, chromaFormatIdc);
        var data = samples[0];
        var count = columns * rows;
        var fileType = WriteBox("ftyp", [.. "heic"u8, 0, 0, 0, 0, .. "heic"u8, .. "mif1"u8, .. "miaf"u8]);
        var handler = WriteFullBox("hdlr", 0, 0, [0, 0, 0, 0, .. "pict"u8, .. new byte[12], 0]);
        var primaryItem = WriteFullBox("pitm", 0, 0, [0, 1]);

        // Item 1 is the grid, items 2 to count + 1 its tiles in row-major order (ISO/IEC 23008-12, section 6.6.2.3)
        var entries = new List<byte>(UInt16(count + 1));
        entries.AddRange(WriteFullBox("infe", 2, 0, [0, 1, 0, 0, .. "grid"u8, 0]));
        var references = new List<byte>([.. UInt16(1), .. UInt16(count)]);
        var associations = new List<byte>([.. UInt32(count + 1), 0, 1, 1, 0x03]);
        for (var i = 0; i < count; i++)
        {
            entries.AddRange(WriteFullBox("infe", 2, 0, [.. UInt16(i + 2), 0, 0, .. "hvc1"u8, 0]));
            references.AddRange(UInt16(i + 2));
            associations.AddRange([.. UInt16(i + 2), 2, 0x01, 0x82]);
        }

        var itemInformation = WriteFullBox("iinf", 0, 0, [.. entries]);
        var itemReferences = WriteFullBox("iref", 0, 0, WriteBox("dimg", [.. references]));
        var tileSize = WriteFullBox("ispe", 0, 0, [.. UInt32(tileWidth), .. UInt32(tileHeight)]);
        var gridSize = WriteFullBox("ispe", 0, 0, [.. UInt32(width), .. UInt32(height)]);
        var properties = WriteBox("iprp", [.. WriteBox("ipco", [.. tileSize, .. configuration, .. gridSize]), .. WriteFullBox("ipma", 0, 0, [.. associations])]);

        // The ImageGrid structure: version 0, 16-bit sizes
        byte[] grid = [0, 0, (byte)(rows - 1), (byte)(columns - 1), .. UInt16(width), .. UInt16(height)];

        // The offsets of the items are in the payload of the 'mdat' box, which follows the 'meta' box
        byte[] BuildMeta(int dataOffset)
        {
            var location = new List<byte>([0x44, 0x00, .. UInt16(count + 1), 0, 1, 0, 0, 0, 1, .. UInt32(dataOffset), .. UInt32(grid.Length)]);
            for (var i = 0; i < count; i++)
            {
                location.AddRange([.. UInt16(i + 2), 0, 0, 0, 1, .. UInt32(dataOffset + grid.Length + (i * data.Length)), .. UInt32(data.Length)]);
            }

            return WriteFullBox("meta", 0, 0, [.. handler, .. primaryItem, .. WriteFullBox("iloc", 0, 0, [.. location]), .. itemInformation, .. itemReferences, .. properties]);
        }

        var meta = BuildMeta(fileType.Length + BuildMeta(0).Length + 8);
        var payload = new List<byte>(grid);
        for (var i = 0; i < count; i++)
        {
            payload.AddRange(data);
        }

        return [.. fileType, .. meta, .. WriteBox("mdat", [.. payload])];
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
