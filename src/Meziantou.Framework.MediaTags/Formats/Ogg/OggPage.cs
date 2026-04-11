using System.Buffers.Binary;
using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Formats.Ogg;

internal sealed class OggPage
{
    public byte Version { get; set; }
    public byte HeaderType { get; set; }
    public long GranulePosition { get; set; }
    public uint SerialNumber { get; set; }
    public uint PageSequenceNumber { get; set; }
    public byte[] SegmentTable { get; set; } = [];
    public byte[] Data { get; set; } = [];

    public const byte HeaderTypeContinued = 0x01;
    public const byte HeaderTypeBeginOfStream = 0x02;
    public const byte HeaderTypeEndOfStream = 0x04;

    public static OggPage? Read(Stream stream)
    {
        Span<byte> headerBuf = stackalloc byte[27];
        if (stream.ReadAtLeast(headerBuf, 27, throwOnEndOfStream: false) < 27)
            return null;

        // Check "OggS" magic
        if (headerBuf[0] != 'O' || headerBuf[1] != 'g' || headerBuf[2] != 'g' || headerBuf[3] != 'S')
            return null;

        var page = new OggPage
        {
            Version = headerBuf[4],
            HeaderType = headerBuf[5],
            GranulePosition = BinaryPrimitives.ReadInt64LittleEndian(headerBuf[6..]),
            SerialNumber = BinaryPrimitives.ReadUInt32LittleEndian(headerBuf[14..]),
            PageSequenceNumber = BinaryPrimitives.ReadUInt32LittleEndian(headerBuf[18..]),
            // CRC at bytes 22-25 (we'll compute when writing)
        };

        var numSegments = headerBuf[26];
        page.SegmentTable = new byte[numSegments];
        if (stream.ReadAtLeast(page.SegmentTable, numSegments, throwOnEndOfStream: false) < numSegments)
            return null;

        var dataSize = 0;
        foreach (var seg in page.SegmentTable)
            dataSize += seg;

        page.Data = new byte[dataSize];
        if (dataSize > 0 && stream.ReadAtLeast(page.Data, dataSize, throwOnEndOfStream: false) < dataSize)
            return null;

        return page;
    }

    public void Write(Stream stream)
    {
        // Rebuild segment table from data
        var pageBytes = Serialize();
        stream.Write(pageBytes);
    }

    public byte[] Serialize()
    {
        var headerSize = 27 + SegmentTable.Length;
        var totalSize = headerSize + Data.Length;
        var result = new byte[totalSize];

        // Magic
        result[0] = (byte)'O';
        result[1] = (byte)'g';
        result[2] = (byte)'g';
        result[3] = (byte)'S';
        result[4] = Version;
        result[5] = HeaderType;
        BinaryPrimitives.WriteInt64LittleEndian(result.AsSpan(6), GranulePosition);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(14), SerialNumber);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(18), PageSequenceNumber);
        // CRC placeholder at 22-25 (set to 0 for computation)
        result[22] = 0;
        result[23] = 0;
        result[24] = 0;
        result[25] = 0;
        result[26] = (byte)SegmentTable.Length;
        SegmentTable.CopyTo(result, 27);
        Data.CopyTo(result, headerSize);

        // Compute CRC
        var crc = OggCrc32.Compute(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(22), crc);

        return result;
    }

    /// <summary>
    /// Builds the segment table for a given data size.
    /// </summary>
    public static byte[] BuildSegmentTable(int dataSize)
    {
        var numFullSegments = dataSize / 255;
        var lastSegmentSize = dataSize % 255;

        // If data is an exact multiple of 255, we need a trailing 0-length segment
        var needsTerminator = dataSize > 0 && lastSegmentSize == 0;
        var tableSize = numFullSegments + (needsTerminator ? 1 : (lastSegmentSize > 0 ? 1 : 0));

        if (dataSize == 0)
            tableSize = 1; // At least one segment entry of 0

        var table = new byte[tableSize];
        for (var i = 0; i < numFullSegments; i++)
            table[i] = 255;

        if (needsTerminator)
            table[numFullSegments] = 0;
        else if (lastSegmentSize > 0)
            table[numFullSegments] = (byte)lastSegmentSize;
        else if (dataSize == 0)
            table[0] = 0;

        return table;
    }
}
