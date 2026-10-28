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

    /// <summary>Gets the checksum stored in the page header, as read from the file.</summary>
    public uint Checksum { get; set; }

    public byte[] SegmentTable { get; set; } = [];
    public byte[] Data { get; set; } = [];

    public const byte HeaderTypeContinued = 0x01;
    public const byte HeaderTypeBeginOfStream = 0x02;
    public const byte HeaderTypeEndOfStream = 0x04;

    /// <summary>The number of bytes in a page header before the segment table.</summary>
    public const int FixedHeaderSize = 27;

    public static OggPage? Read(Stream stream)
    {
        Span<byte> headerBuf = stackalloc byte[FixedHeaderSize];
        if (stream.ReadAtLeast(headerBuf, FixedHeaderSize, throwOnEndOfStream: false) < FixedHeaderSize)
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
            Checksum = BinaryPrimitives.ReadUInt32LittleEndian(headerBuf[22..]),
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

    /// <summary>
    /// Recomputes the page checksum and compares it with the one stored in the file.
    /// </summary>
    public bool VerifyChecksum() => ComputeChecksum() == Checksum;

    public void Write(Stream stream)
    {
        var pageBytes = Serialize();
        stream.Write(pageBytes);
    }

    public byte[] Serialize()
    {
        var result = BuildPageBytes();

        // Compute CRC over the page with the checksum field zeroed, then store it.
        var crc = OggCrc32.Compute(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(22), crc);

        return result;
    }

    private uint ComputeChecksum() => OggCrc32.Compute(BuildPageBytes());

    /// <summary>Builds the page bytes with the checksum field zeroed, which is what the checksum is computed over.</summary>
    private byte[] BuildPageBytes()
    {
        var headerSize = FixedHeaderSize + SegmentTable.Length;
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
        // Checksum field stays zero: it is not part of its own computation.
        result[26] = (byte)SegmentTable.Length;
        SegmentTable.CopyTo(result, FixedHeaderSize);
        Data.CopyTo(result, headerSize);

        return result;
    }
}
