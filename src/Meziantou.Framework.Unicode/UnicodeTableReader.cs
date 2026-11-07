using System.Buffers.Binary;

namespace Meziantou.Framework;

/// <summary>Reads the generated tables that are made of fixed-size records of little-endian fields.</summary>
/// <remarks>
/// Every generated table is a span of bytes over static data: reading one never allocates, whatever the build
/// configuration, and does not depend on the byte order of the platform.
/// </remarks>
internal static class UnicodeTableReader
{
    public static int ReadUInt24(ReadOnlySpan<byte> values, int offset)
    {
        return values[offset] | (values[offset + 1] << 8) | (values[offset + 2] << 16);
    }

    public static int ReadInt32(ReadOnlySpan<byte> values, int offset) => BinaryPrimitives.ReadInt32LittleEndian(values[offset..]);

    /// <summary>Finds the record whose first three-byte field is a key, in records sorted by that field.</summary>
    /// <returns>The offset of the record, or -1.</returns>
    public static int FindRecord(ReadOnlySpan<byte> records, int recordSize, int key)
    {
        var low = 0;
        var high = (records.Length / recordSize) - 1;
        while (low <= high)
        {
            var middle = (int)(((uint)low + (uint)high) / 2);
            var value = ReadUInt24(records, middle * recordSize);
            if (key < value)
            {
                high = middle - 1;
            }
            else if (key > value)
            {
                low = middle + 1;
            }
            else
            {
                return middle * recordSize;
            }
        }

        return -1;
    }

    /// <summary>Finds the record whose first two three-byte fields are a pair of keys, in records sorted by those fields.</summary>
    /// <returns>The offset of the record, or -1.</returns>
    public static int FindRecord(ReadOnlySpan<byte> records, int recordSize, int firstKey, int secondKey)
    {
        var low = 0;
        var high = (records.Length / recordSize) - 1;
        while (low <= high)
        {
            var middle = (int)(((uint)low + (uint)high) / 2);
            var offset = middle * recordSize;
            var comparison = firstKey.CompareTo(ReadUInt24(records, offset));
            if (comparison == 0)
            {
                comparison = secondKey.CompareTo(ReadUInt24(records, offset + 3));
            }

            if (comparison < 0)
            {
                high = middle - 1;
            }
            else if (comparison > 0)
            {
                low = middle + 1;
            }
            else
            {
                return offset;
            }
        }

        return -1;
    }
}
