using System.Buffers.Binary;

namespace Meziantou.Framework.FileConversionTool.Mobi;

/// <summary>
/// Reads an index of a book (INDX records): the first record holds the description of the tags (TAGX), and the records
/// that follow it hold the entries, whose positions the IDXT table of each record gives.
/// </summary>
internal static class MobiIndex
{
    // The maximum number of entries of an index, and of values of an entry
    private const int MaxEntryCount = 1024 * 1024;
    private const int MaxValueCount = 1024;

    /// <summary>Reads the entries of the index whose first record is <paramref name="recordIndex"/>.</summary>
    /// <exception cref="InvalidDataException">The index is not valid.</exception>
    public static List<MobiIndexEntry> Read(MobiBook book, int recordIndex)
    {
        var header = book.GetRecord(recordIndex).Span;
        var (headerLength, _, recordCount) = ReadHeader(header);
        if (recordCount > book.RecordCount - recordIndex - 1)
            throw new InvalidDataException("The index of the book has more records than the book");

        var tags = ReadTags(header, headerLength, out var controlByteCount);
        var result = new List<MobiIndexEntry>();
        for (var i = 1; i <= recordCount; i++)
        {
            var record = book.GetRecord(recordIndex + i).Span;
            var (_, idxtOffset, entryCount) = ReadHeader(record);
            if (idxtOffset > record.Length - 4 || !record[idxtOffset..].StartsWith("IDXT"u8) || entryCount > (record.Length - idxtOffset - 4) / 2)
                throw new InvalidDataException("The IDXT table of an index record of the book is not valid");

            if (result.Count + entryCount > MaxEntryCount)
                throw new InvalidDataException("The index of the book has too many entries");

            for (var j = 0; j < entryCount; j++)
            {
                var start = BinaryPrimitives.ReadUInt16BigEndian(record[(idxtOffset + 4 + (j * 2))..]);
                var end = j + 1 < entryCount ? BinaryPrimitives.ReadUInt16BigEndian(record[(idxtOffset + 4 + ((j + 1) * 2))..]) : idxtOffset;
                if (start >= end || end > record.Length)
                    throw new InvalidDataException("An entry of an index record of the book is not valid");

                result.Add(ReadEntry(record[start..end], tags, controlByteCount));
            }
        }

        return result;
    }

    /// <summary>Reads a variable-width integer whose last byte has its high bit set.</summary>
    /// <exception cref="InvalidDataException">The integer is truncated or too large.</exception>
    internal static uint ReadVariableWidthValue(ReadOnlySpan<byte> data, ref int position)
    {
        ulong value = 0;
        for (var i = 0; i < 5; i++)
        {
            if (position >= data.Length)
                throw new InvalidDataException("A value of an index entry of the book is truncated");

            var b = data[position++];
            value = (value << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) is not 0)
                return value <= uint.MaxValue ? (uint)value : throw new InvalidDataException("A value of an index entry of the book is too large");
        }

        throw new InvalidDataException("A value of an index entry of the book is too large");
    }

    private static (int HeaderLength, int IdxtOffset, int Count) ReadHeader(ReadOnlySpan<byte> record)
    {
        if (record.Length < 28 || !record.StartsWith("INDX"u8))
            throw new InvalidDataException("An index record of the book is not valid");

        var headerLength = BinaryPrimitives.ReadUInt32BigEndian(record[4..]);
        var idxtOffset = BinaryPrimitives.ReadUInt32BigEndian(record[20..]);
        var count = BinaryPrimitives.ReadUInt32BigEndian(record[24..]);
        if (headerLength > record.Length || idxtOffset > record.Length || count > MaxEntryCount)
            throw new InvalidDataException("An index record of the book is not valid");

        return ((int)headerLength, (int)idxtOffset, (int)count);
    }

    private static List<(int Tag, int ValuesPerEntry, int Mask, bool EndFlag)> ReadTags(ReadOnlySpan<byte> header, int offset, out int controlByteCount)
    {
        if (offset > header.Length - 12 || !header[offset..].StartsWith("TAGX"u8))
            throw new InvalidDataException("The index of the book has no TAGX table");

        var length = BinaryPrimitives.ReadUInt32BigEndian(header[(offset + 4)..]);
        var count = BinaryPrimitives.ReadUInt32BigEndian(header[(offset + 8)..]);
        if (length < 12 || length > header.Length - offset || count > 16)
            throw new InvalidDataException("The TAGX table of the index of the book is not valid");

        controlByteCount = (int)count;
        var result = new List<(int, int, int, bool)>();
        for (var position = offset + 12; position + 4 <= offset + length; position += 4)
        {
            result.Add((header[position], header[position + 1], header[position + 2], header[position + 3] is 1));
        }

        return result;
    }

    private static MobiIndexEntry ReadEntry(ReadOnlySpan<byte> entry, List<(int Tag, int ValuesPerEntry, int Mask, bool EndFlag)> tags, int controlByteCount)
    {
        var nameLength = entry[0];
        if (1 + nameLength + controlByteCount > entry.Length)
            throw new InvalidDataException("An entry of an index record of the book is truncated");

        var name = entry.Slice(1, nameLength).ToArray();
        var controlBytes = entry.Slice(1 + nameLength, controlByteCount);
        var position = 1 + nameLength + controlByteCount;

        // The control bytes give, for each tag, the number of groups of values, or the number of bytes of its values
        var counts = new List<(int Tag, int? ValueCount, int? ByteCount, int ValuesPerEntry)>();
        var controlByteIndex = 0;
        foreach (var (tag, valuesPerEntry, mask, endFlag) in tags)
        {
            if (endFlag)
            {
                controlByteIndex++;
                continue;
            }

            if (controlByteIndex >= controlBytes.Length || mask is 0)
                continue;

            var value = controlBytes[controlByteIndex] & mask;
            if (value is 0)
                continue;

            if (value == mask && System.Numerics.BitOperations.PopCount((uint)mask) > 1)
            {
                counts.Add((tag, null, (int)Math.Min(ReadVariableWidthValue(entry, ref position), int.MaxValue), valuesPerEntry));
            }
            else
            {
                counts.Add((tag, value >> System.Numerics.BitOperations.TrailingZeroCount(mask), null, valuesPerEntry));
            }
        }

        var result = new Dictionary<int, IReadOnlyList<uint>>();
        foreach (var (tag, valueCount, byteCount, valuesPerEntry) in counts)
        {
            var values = new List<uint>();
            if (valueCount is { } groups)
            {
                if ((long)groups * valuesPerEntry > MaxValueCount)
                    throw new InvalidDataException("An entry of an index record of the book has too many values");

                for (var i = 0; i < groups * valuesPerEntry; i++)
                {
                    values.Add(ReadVariableWidthValue(entry, ref position));
                }
            }
            else
            {
                var end = position + (long)byteCount!.Value;
                while (position < end)
                {
                    if (values.Count >= MaxValueCount)
                        throw new InvalidDataException("An entry of an index record of the book has too many values");

                    values.Add(ReadVariableWidthValue(entry, ref position));
                }
            }

            result.TryAdd(tag, values);
        }

        return new MobiIndexEntry(name, result);
    }
}
