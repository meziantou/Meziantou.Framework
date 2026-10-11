using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>
/// The parts of the ISO base media container that the fuzz tests need: a minimal AVIF file around a raw AV1 stream of the
/// corpus (a still image for one temporal unit, an image sequence otherwise), so that the AV1 vectors are fuzz seeds of the
/// public API, and the location of the media data of a file, so that mutations can change the AV1 payload without breaking
/// the offsets of the container.
/// </summary>
internal static class FuzzAvifContainer
{
    /// <summary>Wraps a raw AV1 stream into an AVIF file.</summary>
    /// <param name="stream">The stream: open bitstream units with their size fields.</param>
    /// <param name="width">The width of the frames.</param>
    /// <param name="height">The height of the frames.</param>
    /// <param name="bitDepth">The bit depth: 8, 10 or 12.</param>
    /// <param name="subsampling">The chroma format: <c>4:0:0</c>, <c>4:2:0</c>, <c>4:2:2</c> or <c>4:4:4</c>.</param>
    public static byte[] Wrap(ReadOnlySpan<byte> stream, int width, int height, int bitDepth, string subsampling)
    {
        var units = SplitTemporalUnits(stream);
        var configuration = WriteBox("av1C", [0x81, 0x00, GetConfigurationFlags(bitDepth, subsampling), 0x00]);
        return units.Count is 1 ? WrapStill(stream, width, height, configuration) : WrapSequence(stream, units, width, height, configuration);
    }

    /// <summary>Returns whether the data start with a file type box.</summary>
    public static bool IsIsoBaseMedia(ReadOnlySpan<byte> data) => data.Length >= 12 && data.Slice(4, 4).SequenceEqual("ftyp"u8);

    /// <summary>Returns whether a file declares the brand of the AVIF image sequences.</summary>
    public static bool IsSequence(ReadOnlySpan<byte> data)
    {
        if (!IsIsoBaseMedia(data))
            return false;

        var size = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(data), (uint)data.Length);
        for (var offset = 8; offset + 4 <= size; offset += 4)
        {
            if (offset is not 12 && data.Slice(offset, 4).SequenceEqual("avis"u8))
                return true;
        }

        return false;
    }

    /// <summary>Gets the ranges of the payloads of the top-level <c>mdat</c> boxes of a file.</summary>
    public static List<(int Offset, int Length)> FindMediaData(ReadOnlySpan<byte> data)
    {
        var result = new List<(int Offset, int Length)>();
        var offset = 0;
        while (offset + 8 <= data.Length)
        {
            ulong size = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            var headerLength = 8;
            if (size is 1)
            {
                if (offset + 16 > data.Length)
                    break;

                size = BinaryPrimitives.ReadUInt64BigEndian(data[(offset + 8)..]);
                headerLength = 16;
            }
            else if (size is 0)
            {
                size = (ulong)(data.Length - offset);
            }

            if (size < (ulong)headerLength || size > (ulong)(data.Length - offset))
                break;

            if (data.Slice(offset + 4, 4).SequenceEqual("mdat"u8) && (int)size > headerLength)
                result.Add((offset + headerLength, (int)size - headerLength));

            offset += (int)size;
        }

        return result;
    }

    // The ranges of the temporal units of a stream: a unit starts at each temporal delimiter
    private static List<(int Offset, int Length)> SplitTemporalUnits(ReadOnlySpan<byte> stream)
    {
        var starts = new List<int>();
        var offset = 0;
        while (offset < stream.Length)
        {
            var header = stream[offset];
            var type = (header >> 3) & 15;
            var position = offset + 1 + ((header >> 2) & 1);
            if ((header & 2) is 0)
                throw new InvalidOperationException("The AV1 stream has a unit without size field.");

            long size = 0;
            for (var i = 0; i < 8; i++)
            {
                var value = stream[position++];
                size |= (long)(value & 0x7F) << (7 * i);
                if ((value & 0x80) is 0)
                    break;
            }

            if (type is 2 || starts.Count is 0)
                starts.Add(offset);

            offset = checked(position + (int)size);
        }

        var units = new List<(int Offset, int Length)>();
        for (var i = 0; i < starts.Count; i++)
        {
            units.Add((starts[i], (i + 1 < starts.Count ? starts[i + 1] : stream.Length) - starts[i]));
        }

        return units;
    }

    private static byte GetConfigurationFlags(int bitDepth, string subsampling)
    {
        var flags = subsampling switch
        {
            "4:0:0" => 0b0001_1100,
            "4:2:0" => 0b0000_1100,
            "4:2:2" => 0b0000_1000,
            "4:4:4" => 0,
            _ => throw new ArgumentException($"Unknown subsampling '{subsampling}'.", nameof(subsampling)),
        };

        if (bitDepth > 8)
            flags |= 0x40;

        if (bitDepth is 12)
            flags |= 0x20;

        return (byte)flags;
    }

    // ISO/IEC 23008-12: a primary item of type 'av01' with its size and its configuration
    private static byte[] WrapStill(ReadOnlySpan<byte> stream, int width, int height, byte[] configuration)
    {
        var fileType = WriteBox("ftyp", [.. "avif"u8, 0, 0, 0, 0, .. "avif"u8, .. "mif1"u8, .. "miaf"u8]);
        var handler = WriteFullBox("hdlr", 0, 0, [0, 0, 0, 0, .. "pict"u8, .. new byte[12], 0]);
        var primaryItem = WriteFullBox("pitm", 0, 0, [0, 1]);
        var itemInformation = WriteFullBox("iinf", 0, 0, [0, 1, .. WriteFullBox("infe", 2, 0, [0, 1, 0, 0, .. "av01"u8, 0])]);
        var size = WriteFullBox("ispe", 0, 0, [.. UInt32(width), .. UInt32(height)]);
        var properties = WriteBox("iprp", [.. WriteBox("ipco", [.. size, .. configuration]), .. WriteFullBox("ipma", 0, 0, [0, 0, 0, 1, 0, 1, 2, 0x01, 0x82])]);

        // The offset of the item is the offset of the payload of the 'mdat' box, which follows the 'meta' box
        var dataLength = stream.Length;
        byte[] BuildMeta(int dataOffset)
        {
            var location = WriteFullBox("iloc", 0, 0, [0x44, 0x00, 0, 1, 0, 1, 0, 0, 0, 1, .. UInt32(dataOffset), .. UInt32(dataLength)]);
            return WriteFullBox("meta", 0, 0, [.. handler, .. primaryItem, .. location, .. itemInformation, .. properties]);
        }

        var meta = BuildMeta(fileType.Length + BuildMeta(0).Length + 8);
        return [.. fileType, .. meta, .. WriteBox("mdat", stream)];
    }

    // ISO/IEC 14496-12: one 'pict' track whose samples are the temporal units, in one chunk
    private static byte[] WrapSequence(ReadOnlySpan<byte> stream, List<(int Offset, int Length)> units, int width, int height, byte[] configuration)
    {
        var fileType = WriteBox("ftyp", [.. "avis"u8, 0, 0, 0, 0, .. "avis"u8, .. "msf1"u8, .. "iso8"u8, .. "miaf"u8]);
        var count = units.Count;
        byte[] BuildMovie(int dataOffset)
        {
            byte[] sampleEntry =
            [
                0, 0, 0, 0, 0, 0, 0, 1, .. new byte[16], .. UInt16(width), .. UInt16(height), 0, 0x48, 0, 0, 0, 0x48, 0, 0, 0, 0, 0, 0, 0, 1, .. new byte[32], 0, 0x18, 0xFF, 0xFF,
                .. configuration,
            ];
            var sizes = new List<byte>();
            foreach (var (_, length) in units)
            {
                sizes.AddRange(UInt32(length));
            }

            var sampleTable = WriteBox("stbl",
            [
                .. WriteFullBox("stsd", 0, 0, [0, 0, 0, 1, .. WriteBox("av01", sampleEntry)]),
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
        return [.. fileType, .. movie, .. WriteBox("mdat", stream)];
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
