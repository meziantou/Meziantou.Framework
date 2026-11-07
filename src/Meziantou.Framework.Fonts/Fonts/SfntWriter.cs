using System.Buffers.Binary;
using System.Numerics;

namespace Meziantou.Framework.Fonts;

/// <summary>Writes the table directory of the font a compressed container is expanded to.</summary>
internal static class SfntWriter
{
    public const int HeaderLength = 12;
    public const int RecordLength = 16;

    /// <summary>Gets the length of a table followed by its padding: the tables of a font start at multiples of four bytes.</summary>
    public static long Pad(long length) => (length + 3) & ~3L;

    /// <summary>Gets the length of the directory of a face.</summary>
    public static long GetDirectoryLength(int tableCount) => HeaderLength + ((long)tableCount * RecordLength);

    /// <summary>Writes the header of a directory: OpenType specification, "Table Directory".</summary>
    public static void WriteHeader(Span<byte> destination, uint version, int tableCount)
    {
        // The three search fields describe the largest power of two that is not more than the number of tables.
        var entrySelector = tableCount == 0 ? 0 : BitOperations.Log2((uint)tableCount);
        var searchRange = tableCount == 0 ? 0 : RecordLength << entrySelector;
        BinaryPrimitives.WriteUInt32BigEndian(destination, version);
        BinaryPrimitives.WriteUInt16BigEndian(destination[4..], (ushort)tableCount);
        BinaryPrimitives.WriteUInt16BigEndian(destination[6..], (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(destination[8..], (ushort)entrySelector);
        BinaryPrimitives.WriteUInt16BigEndian(destination[10..], (ushort)((tableCount * RecordLength) - searchRange));
    }

    /// <summary>Writes the record of a table.</summary>
    public static void WriteRecord(Span<byte> destination, uint tag, uint checksum, long offset, long length)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination, tag);
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], checksum);
        BinaryPrimitives.WriteUInt32BigEndian(destination[8..], (uint)offset);
        BinaryPrimitives.WriteUInt32BigEndian(destination[12..], (uint)length);
    }
}
