using System.Buffers.Binary;

namespace Meziantou.Framework.MediaTags.Tests;

/// <summary>
/// A minimal OGG parser used by the tests to inspect what the library actually wrote.
/// </summary>
/// <remarks>
/// It deliberately does not use the library's own page reader: a reader and a writer that agree on a wrong
/// layout would otherwise verify each other and every round-trip test would pass while producing files no
/// other player accepts.
/// </remarks>
internal static class OggPageInspector
{
    public sealed record Page(
        uint SerialNumber,
        uint SequenceNumber,
        long GranulePosition,
        byte HeaderType,
        uint StoredChecksum,
        byte[] BytesWithZeroedChecksum,
        byte[] SegmentTable,
        byte[] Data);

    public static List<Page> ReadPages(byte[] file)
    {
        var pages = new List<Page>();
        var position = 0;

        while (position + 27 <= file.Length)
        {
            if (file[position] != 'O' || file[position + 1] != 'g' || file[position + 2] != 'g' || file[position + 3] != 'S')
                break;

            var segmentCount = file[position + 26];
            var tableStart = position + 27;
            if (tableStart + segmentCount > file.Length)
                break;

            var segmentTable = file.AsSpan(tableStart, segmentCount).ToArray();
            var dataStart = tableStart + segmentCount;
            var dataLength = 0;
            foreach (var segment in segmentTable)
            {
                dataLength += segment;
            }

            if (dataStart + dataLength > file.Length)
                break;

            var pageEnd = dataStart + dataLength;
            var bytes = file.AsSpan(position, pageEnd - position).ToArray();
            var storedChecksum = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(22));
            Array.Clear(bytes, 22, 4);

            pages.Add(new Page(
                SerialNumber: BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(position + 14)),
                SequenceNumber: BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(position + 18)),
                GranulePosition: BinaryPrimitives.ReadInt64LittleEndian(file.AsSpan(position + 6)),
                HeaderType: file[position + 5],
                StoredChecksum: storedChecksum,
                BytesWithZeroedChecksum: bytes,
                SegmentTable: segmentTable,
                Data: file.AsSpan(dataStart, dataLength).ToArray()));

            position = pageEnd;
        }

        return pages;
    }

    /// <summary>Assembles the packets of the whole file, in order.</summary>
    public static List<byte[]> ReadPackets(byte[] file)
    {
        var packets = new List<byte[]>();
        var current = new List<byte>();

        foreach (var page in ReadPages(file))
        {
            var offset = 0;
            foreach (var segmentLength in page.SegmentTable)
            {
                current.AddRange(page.Data.AsSpan(offset, segmentLength).ToArray());
                offset += segmentLength;

                if (segmentLength < 255)
                {
                    packets.Add(current.ToArray());
                    current = [];
                }
            }
        }

        return packets;
    }

    /// <summary>Finds the first packet starting with the given prefix.</summary>
    public static byte[]? FindPacket(byte[] file, ReadOnlySpan<byte> prefix)
    {
        foreach (var packet in ReadPackets(file))
        {
            if (packet.AsSpan().StartsWith(prefix))
                return packet;
        }

        return null;
    }
}
