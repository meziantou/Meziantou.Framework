using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// An INDEX of a CFF table: an array of variable-sized objects. Adobe Technical Note #5176, "The Compact Font Format
/// Specification", section 5.
/// </summary>
/// <remarks>
/// The structure keeps positions in the table, never the bytes. The header and the size of the offset array are
/// validated when the INDEX is read; the offsets of an object are validated when the object is read.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly struct CffIndex
{
    private readonly int _offsets;
    private readonly int _offsetSize;
    private readonly int _data;
    private readonly int _dataLength;

    private CffIndex(int count, int offsets, int offsetSize, int data, int dataLength, int end)
    {
        Count = count;
        _offsets = offsets;
        _offsetSize = offsetSize;
        _data = data;
        _dataLength = dataLength;
        End = end;
    }

    /// <summary>Gets the number of objects.</summary>
    public int Count { get; }

    /// <summary>Gets the position that follows the INDEX in the table.</summary>
    public int End { get; }

    /// <summary>Reads the header of an INDEX at a position of the table.</summary>
    public static CffIndex Read(ReadOnlySpan<byte> table, long position)
    {
        if (position < 0 || position > table.Length - 2)
            throw FontErrors.InvalidTable(FontTags.Cff, "has an INDEX outside its bounds.");

        var start = (int)position;
        var count = FontBinary.UInt16(table, start);
        if (count == 0)
            return new CffIndex(0, 0, 0, 0, 0, start + 2);

        var offsetSize = FontBinary.UInt8(table, start + 2);
        if (offsetSize is < 1 or > 4)
            throw FontErrors.InvalidTable(FontTags.Cff, $"has an INDEX with offsets of {offsetSize} bytes.");

        // The offsets are relative to the byte that precedes the data: the first one is 1, the last one gives the
        // length of the data.
        var offsets = start + 3;
        var offsetsLength = ((long)count + 1) * offsetSize;
        FontBinary.EnsureRange(table, offsets, offsetsLength);
        var data = offsets + (int)offsetsLength;
        var last = ReadOffset(table, offsets + (count * offsetSize), offsetSize);
        if (last < 1 || data + (last - 1) > table.Length)
            throw FontErrors.InvalidTable(FontTags.Cff, "has an INDEX whose data lies outside the table.");

        return new CffIndex(count, offsets, offsetSize, data, (int)(last - 1), data + (int)(last - 1));
    }

    /// <summary>Gets an object.</summary>
    /// <exception cref="InvalidImageContentException">The index is not an object of the INDEX, or its offsets are invalid.</exception>
    public ReadOnlySpan<byte> Get(ReadOnlySpan<byte> table, int index)
    {
        if ((uint)index >= (uint)Count)
            throw FontErrors.InvalidTable(FontTags.Cff, "refers to an object that is not in an INDEX.");

        var start = ReadOffset(table, _offsets + (index * _offsetSize), _offsetSize);
        var end = ReadOffset(table, _offsets + ((index + 1) * _offsetSize), _offsetSize);
        if (start < 1 || end < start || end - 1 > _dataLength)
            throw FontErrors.InvalidTable(FontTags.Cff, "has an INDEX with invalid offsets.");

        return table.Slice(_data + (int)(start - 1), (int)(end - start));
    }

    private static long ReadOffset(ReadOnlySpan<byte> table, int position, int size)
    {
        long value = 0;
        for (var i = 0; i < size; i++)
        {
            value = (value << 8) | (uint)FontBinary.UInt8(table, position + i);
        }

        return value;
    }
}
