using System.Buffers.Binary;

namespace Meziantou.Framework.Pdf.Rendering.Fonts;

/// <summary>
/// A subtable of the <c>cmap</c> table of a TrueType font program (OpenType specification, "cmap"): the formats 0, 2, 4,
/// 6, 12 and 13. A well-formed subtable is read in place when a character is looked up: its segments or groups are sorted
/// and disjoint, so a lookup is a binary search, and a large range of characters costs no memory. A subtable whose
/// segments or groups are not sorted or overlap is expanded into a dictionary, with a limit on the characters, the first
/// segment that maps a character winning.
/// </summary>
internal sealed class TrueTypeCmapSubtable
{
    // The characters a malformed subtable is expanded to: overlapping segments or groups cannot make the reading longer
    private const int MaxExpandedCodes = 1 << 20;

    // The characters a group of the formats 12 and 13 maps: the glyph identifiers have 16 bits
    private const int MaxGroupLength = 65536;

    private const int MaxGroups = 65536;

    private readonly byte[] _data;
    private readonly int _offset;
    private readonly int _format;
    private readonly int _count;
    private readonly Dictionary<int, int>? _expanded;

    private TrueTypeCmapSubtable(byte[] data, int offset, int format, int count, Dictionary<int, int>? expanded = null)
    {
        _data = data;
        _offset = offset;
        _format = format;
        _count = count;
        _expanded = expanded;
    }

    /// <summary>Reads the subtable at an offset of the <c>cmap</c> table; <see langword="null"/> for a format the reader does not support or a header that passes the end of the table.</summary>
    public static TrueTypeCmapSubtable? Read(byte[] cmap, int offset)
    {
        if (offset < 0 || offset + 2 > cmap.Length)
            return null;

        var format = ReadUInt16(cmap, offset);
        switch (format)
        {
            case 0:
                return offset + 6 <= cmap.Length ? new TrueTypeCmapSubtable(cmap, offset, format, Math.Min(256, cmap.Length - offset - 6)) : null;

            case 2:
                // The keys of the 256 high bytes, then the subheaders and the glyph indexes they point to
                return offset + 6 + 512 <= cmap.Length ? new TrueTypeCmapSubtable(cmap, offset, format, 0) : null;

            case 4:
                {
                    if (offset + 14 > cmap.Length)
                        return null;

                    var segments = ReadUInt16(cmap, offset + 6) / 2;
                    if (offset + 16 + (segments * 8) > cmap.Length)
                    {
                        // The arrays pass the end of the table: the segments that are complete
                        segments = Math.Max(0, (cmap.Length - offset - 16) / 8);
                    }

                    var table = new TrueTypeCmapSubtable(cmap, offset, format, segments);
                    return table.AreSegmentsSorted() ? table : new TrueTypeCmapSubtable(cmap, offset, format, segments, table.ExpandSegments());
                }

            case 6:
                {
                    if (offset + 10 > cmap.Length)
                        return null;

                    var count = Math.Min((int)ReadUInt16(cmap, offset + 8), (cmap.Length - offset - 10) / 2);
                    return new TrueTypeCmapSubtable(cmap, offset, format, count);
                }

            case 12 or 13:
                {
                    if (offset + 16 > cmap.Length)
                        return null;

                    var groups = (int)Math.Min(Math.Min(ReadUInt32(cmap, offset + 12), MaxGroups), (uint)((cmap.Length - offset - 16) / 12));
                    var table = new TrueTypeCmapSubtable(cmap, offset, format, groups);
                    return table.AreGroupsSorted() ? table : new TrueTypeCmapSubtable(cmap, offset, format, groups, table.ExpandGroups());
                }

            default:
                return null;
        }
    }

    /// <summary>Gets the glyph of a character; 0 when the subtable does not map it.</summary>
    public int Map(int character)
    {
        if (_expanded is not null)
            return _expanded.GetValueOrDefault(character);

        return _format switch
        {
            0 => (uint)character < (uint)_count ? _data[_offset + 6 + character] : 0,
            2 => MapHighByte(character),
            4 => (uint)character <= 0xFFFF ? MapSegments(character) : 0,
            6 => MapTrimmed(character),
            _ => MapGroups(character),
        };
    }

    private int MapTrimmed(int character)
    {
        var index = character - ReadUInt16(_data, _offset + 6);
        return (uint)index < (uint)_count ? ReadUInt16(_data, _offset + 10 + (index * 2)) : 0;
    }

    /// <summary>
    /// Format 2, the mixed 8- and 16-bit encodings of the CJK code pages: the high byte selects a subheader, the subheader
    /// 0 for the bytes that are characters on their own; the low byte is looked up in the range of the subheader.
    /// </summary>
    private int MapHighByte(int character)
    {
        if ((uint)character > 0xFFFF)
            return 0;

        int key;
        int low;
        if (character < 256)
        {
            key = ReadUInt16(_data, _offset + 6 + (character * 2));
            if (key != 0)
                return 0;

            low = character;
        }
        else
        {
            key = ReadUInt16(_data, _offset + 6 + ((character >> 8) * 2));
            if (key == 0)
                return 0;

            low = character & 0xFF;
        }

        var subHeader = _offset + 6 + 512 + key;
        if (subHeader + 8 > _data.Length)
            return 0;

        var firstCode = ReadUInt16(_data, subHeader);
        var entryCount = ReadUInt16(_data, subHeader + 2);
        var delta = BinaryPrimitives.ReadInt16BigEndian(_data.AsSpan(subHeader + 4));
        var rangeOffset = ReadUInt16(_data, subHeader + 6);
        if (low < firstCode || low >= firstCode + entryCount)
            return 0;

        // The range offset counts the bytes from its own position to the glyph index of the first code
        var position = subHeader + 6 + rangeOffset + ((low - firstCode) * 2);
        if (position + 2 > _data.Length)
            return 0;

        var glyph = ReadUInt16(_data, position);
        return glyph == 0 ? 0 : (glyph + delta) & 0xFFFF;
    }

    private int MapSegments(int character)
    {
        // The first segment whose end is not before the character
        var ends = _offset + 14;
        var lower = 0;
        var upper = _count - 1;
        while (lower < upper)
        {
            var middle = lower + ((upper - lower) / 2);
            if (ReadUInt16(_data, ends + (middle * 2)) < character)
            {
                lower = middle + 1;
            }
            else
            {
                upper = middle;
            }
        }

        return _count > 0 ? MapSegment(lower, character) : 0;
    }

    private int MapSegment(int segment, int character)
    {
        var ends = _offset + 14;
        var starts = ends + (_count * 2) + 2;
        var deltas = starts + (_count * 2);
        var rangeOffsets = deltas + (_count * 2);
        var end = ReadUInt16(_data, ends + (segment * 2));
        var start = ReadUInt16(_data, starts + (segment * 2));
        if (character < start || character > end || character == 0xFFFF)
            return 0;

        var delta = BinaryPrimitives.ReadInt16BigEndian(_data.AsSpan(deltas + (segment * 2)));
        var rangeOffsetPosition = rangeOffsets + (segment * 2);
        var rangeOffset = ReadUInt16(_data, rangeOffsetPosition);
        if (rangeOffset == 0)
            return (character + delta) & 0xFFFF;

        var position = rangeOffsetPosition + rangeOffset + ((character - start) * 2);
        if (position + 2 > _data.Length)
            return 0;

        var glyph = ReadUInt16(_data, position);
        return glyph == 0 ? 0 : (glyph + delta) & 0xFFFF;
    }

    private int MapGroups(int character)
    {
        var code = (uint)character;
        var lower = 0;
        var upper = _count - 1;
        while (lower <= upper)
        {
            var middle = lower + ((upper - lower) / 2);
            var group = _offset + 16 + (middle * 12);
            var start = ReadUInt32(_data, group);
            var end = ReadUInt32(_data, group + 4);
            if (code < start)
            {
                upper = middle - 1;
            }
            else if (code > end)
            {
                lower = middle + 1;
            }
            else
            {
                return GetGroupGlyph(group, start, code);
            }
        }

        return 0;
    }

    /// <summary>The glyph of a character of a group: consecutive glyphs (format 12), or the same glyph for all (format 13).</summary>
    private int GetGroupGlyph(int group, uint start, uint code)
    {
        if (code - start >= MaxGroupLength || code > 0x10FFFF)
            return 0;

        var glyph = ReadUInt32(_data, group + 8);
        return (int)Math.Min(_format == 13 ? glyph : glyph + (code - start), int.MaxValue);
    }

    private bool AreSegmentsSorted()
    {
        var ends = _offset + 14;
        var starts = ends + (_count * 2) + 2;
        var previousEnd = -1;
        for (var i = 0; i < _count; i++)
        {
            var end = ReadUInt16(_data, ends + (i * 2));
            var start = ReadUInt16(_data, starts + (i * 2));
            if (start > end || start <= previousEnd)
                return false;

            previousEnd = end;
        }

        return true;
    }

    private bool AreGroupsSorted()
    {
        long previousEnd = -1;
        for (var i = 0; i < _count; i++)
        {
            var group = _offset + 16 + (i * 12);
            var start = ReadUInt32(_data, group);
            var end = ReadUInt32(_data, group + 4);
            if (start > end || start <= previousEnd)
                return false;

            previousEnd = end;
        }

        return true;
    }

    private Dictionary<int, int> ExpandSegments()
    {
        var map = new Dictionary<int, int>();
        var ends = _offset + 14;
        var starts = ends + (_count * 2) + 2;
        var work = 0;
        for (var segment = 0; segment < _count; segment++)
        {
            var end = ReadUInt16(_data, ends + (segment * 2));
            for (var code = (int)ReadUInt16(_data, starts + (segment * 2)); code <= end && code != 0xFFFF; code++)
            {
                if (++work > MaxExpandedCodes)
                    return map;

                if (!map.ContainsKey(code) && MapSegment(segment, code) is not 0 and var glyph)
                {
                    map.Add(code, glyph);
                }
            }
        }

        return map;
    }

    private Dictionary<int, int> ExpandGroups()
    {
        var map = new Dictionary<int, int>();
        var work = 0;
        for (var i = 0; i < _count; i++)
        {
            var group = _offset + 16 + (i * 12);
            var start = ReadUInt32(_data, group);
            var end = ReadUInt32(_data, group + 4);
            for (var code = start; code <= end && code - start < MaxGroupLength && code <= 0x10FFFF; code++)
            {
                if (++work > MaxExpandedCodes)
                    return map;

                map.TryAdd((int)code, GetGroupGlyph(group, start, code));
            }
        }

        return map;
    }

    private static ushort ReadUInt16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset));

    private static uint ReadUInt32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
}
