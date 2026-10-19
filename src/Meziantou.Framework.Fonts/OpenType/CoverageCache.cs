namespace Meziantou.Framework.Fonts;

/// <summary>
/// The coverage indexes of a coverage table, kept by pages of 256 glyphs built the first time a glyph of the page is
/// looked up: the glyphs of a text are looked up in the coverage of each subtable of each lookup, again and again.
/// </summary>
/// <remarks>
/// <para>Like the <see cref="LookupDigests"/> that keep it, a cache is used by one shaping at a time.</para>
/// <para>
/// A page of a table whose glyphs (format 1) or ranges (format 2) are in increasing order, as the specification requires,
/// is built in one pass over the entries of the page. The page of another table is built glyph by glyph, by the search
/// of <see cref="LayoutCommon.GetCoverage"/>, so that it has the indexes that search gives.
/// </para>
/// </remarks>
internal sealed class CoverageCache
{
    private const ushort NotCovered = ushort.MaxValue;
    private const int PageLength = 256;

    // Whether the entries of the table are in order: not checked yet, in order, or not (or the table is malformed).
    private const byte Unchecked = 0;
    private const byte Ordered = 1;
    private const byte Unordered = 2;

    // The directory has one entry per page up to the last page looked up.
    private ushort[]?[] _pages = [];
    private byte _order;

    public CoverageCache(int offset)
    {
        Offset = offset;
    }

    /// <summary>Gets the offset of the coverage table in its layout table.</summary>
    public int Offset { get; }

    /// <summary>Gets the coverage index of a glyph, or <see cref="LayoutCommon.NotCovered"/>.</summary>
    /// <exception cref="InvalidFontException">The coverage table is malformed.</exception>
    public int Get(ReadOnlySpan<byte> table, int glyph)
    {
        if ((uint)glyph > 0xFFFF)
            return LayoutCommon.GetCoverage(table, Offset, glyph);

        var pageIndex = glyph >> 8;
        if (pageIndex >= _pages.Length)
        {
            Array.Resize(ref _pages, pageIndex + 1);
        }

        var page = _pages[pageIndex];
        if (page is null)
        {
            page = new ushort[PageLength];
            var first = glyph & ~0xFF;
            if (_order == Unchecked)
            {
                _order = IsOrdered(table) ? Ordered : Unordered;
            }

            if (_order == Ordered)
            {
                Fill(table, first, page);
            }
            else
            {
                try
                {
                    for (var i = 0; i < page.Length; i++)
                    {
                        var index = LayoutCommon.GetCoverage(table, Offset, first + i);
                        page[i] = index is >= 0 and < NotCovered ? (ushort)index : NotCovered;
                    }
                }
                catch (InvalidFontException)
                {
                    // A malformed table fails for the glyphs that read past its end, as it does when they are looked up.
                    return LayoutCommon.GetCoverage(table, Offset, glyph);
                }
            }

            _pages[pageIndex] = page;
        }

        var value = page[glyph & 0xFF];
        return value == NotCovered ? LayoutCommon.NotCovered : value;
    }

    /// <summary>
    /// Determines whether the table is complete and its glyphs are in strictly increasing order (format 1), or its ranges
    /// are in increasing order without overlap (format 2); a table of another format covers nothing.
    /// </summary>
    private bool IsOrdered(ReadOnlySpan<byte> table)
    {
        try
        {
            var format = FontBinary.UInt16(table, Offset);
            var count = FontBinary.UInt16(table, Offset + 2);
            switch (format)
            {
                case 1:
                    {
                        var glyphs = FontBinary.Slice(table, Offset + 4, count * 2L);
                        for (var i = 1; i < count; i++)
                        {
                            if (FontBinary.UInt16(glyphs, (i - 1) * 2) >= FontBinary.UInt16(glyphs, i * 2))
                                return false;
                        }

                        return true;
                    }

                case 2:
                    {
                        var ranges = FontBinary.Slice(table, Offset + 4, count * 6L);
                        for (var i = 0; i < count; i++)
                        {
                            var start = FontBinary.UInt16(ranges, i * 6);
                            var end = FontBinary.UInt16(ranges, (i * 6) + 2);
                            if (start > end || (i > 0 && FontBinary.UInt16(ranges, (i * 6) - 4) >= start))
                                return false;
                        }

                        return true;
                    }

                default:
                    return true;
            }
        }
        catch (InvalidFontException)
        {
            return false;
        }
    }

    /// <summary>Fills a page of the coverage of an ordered table: the entries of the table from the first one of the page.</summary>
    private void Fill(ReadOnlySpan<byte> table, int first, ushort[] page)
    {
        page.AsSpan().Fill(NotCovered);
        var end = first + PageLength;
        var count = FontBinary.UInt16(table, Offset + 2);
        switch (FontBinary.UInt16(table, Offset))
        {
            case 1:
                {
                    var glyphs = FontBinary.Slice(table, Offset + 4, count * 2L);
                    for (var i = LowerBound(glyphs, 2, 0, count, first); i < count; i++)
                    {
                        var glyph = FontBinary.UInt16(glyphs, i * 2);
                        if (glyph >= end)
                            break;

                        page[glyph - first] = (ushort)i;
                    }

                    break;
                }

            case 2:
                {
                    var ranges = FontBinary.Slice(table, Offset + 4, count * 6L);
                    for (var i = LowerBound(ranges, 6, 2, count, first); i < count; i++)
                    {
                        var start = FontBinary.UInt16(ranges, i * 6);
                        if (start >= end)
                            break;

                        var last = Math.Min(FontBinary.UInt16(ranges, (i * 6) + 2), end - 1);
                        var startIndex = FontBinary.UInt16(ranges, (i * 6) + 4);
                        for (var glyph = Math.Max(start, first); glyph <= last; glyph++)
                        {
                            var index = startIndex + (glyph - start);
                            page[glyph - first] = index < NotCovered ? (ushort)index : NotCovered;
                        }
                    }

                    break;
                }
        }
    }

    /// <summary>Gets the first record whose field (a glyph, or the last glyph of a range) is at least a glyph.</summary>
    private static int LowerBound(ReadOnlySpan<byte> records, int recordLength, int field, int count, int glyph)
    {
        var low = 0;
        var high = count;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (FontBinary.UInt16(records, (middle * recordLength) + field) < glyph)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
