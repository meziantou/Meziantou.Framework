namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the tables the OpenType Layout tables share: OpenType specification, "OpenType Layout Common Table Formats"
/// (coverage and class definition tables).
/// </summary>
internal static class LayoutCommon
{
    /// <summary>The value of a coverage index for a glyph that is not covered.</summary>
    public const int NotCovered = -1;

    /// <summary>Gets the coverage index of a glyph in a coverage table, or <see cref="NotCovered"/>.</summary>
    public static int GetCoverage(ReadOnlySpan<byte> table, int offset, int glyph)
    {
        var format = FontBinary.UInt16(table, offset);
        var count = FontBinary.UInt16(table, offset + 2);
        var low = 0;
        var high = count - 1;
        switch (format)
        {
            // A sorted list of glyphs: the index of the glyph in the list.
            case 1:
                while (low <= high)
                {
                    var middle = (low + high) >>> 1;
                    var value = FontBinary.UInt16(table, offset + 4 + (middle * 2));
                    if (value == glyph)
                        return middle;

                    if (value < glyph)
                    {
                        low = middle + 1;
                    }
                    else
                    {
                        high = middle - 1;
                    }
                }

                return NotCovered;

            // Sorted ranges of glyphs, each with the index of its first glyph.
            case 2:
                while (low <= high)
                {
                    var middle = (low + high) >>> 1;
                    var record = offset + 4 + (middle * 6);
                    var start = FontBinary.UInt16(table, record);
                    if (glyph < start)
                    {
                        high = middle - 1;
                    }
                    else if (glyph > FontBinary.UInt16(table, record + 2))
                    {
                        low = middle + 1;
                    }
                    else
                    {
                        return FontBinary.UInt16(table, record + 4) + (glyph - start);
                    }
                }

                return NotCovered;

            default:
                return NotCovered;
        }
    }

    /// <summary>Gets the class of a glyph in a class definition table; 0 for a glyph the table does not list.</summary>
    public static int GetClass(ReadOnlySpan<byte> table, int offset, int glyph)
    {
        switch (FontBinary.UInt16(table, offset))
        {
            // The classes of a range of consecutive glyphs.
            case 1:
                {
                    var index = glyph - FontBinary.UInt16(table, offset + 2);
                    return index >= 0 && index < FontBinary.UInt16(table, offset + 4) ? FontBinary.UInt16(table, offset + 6 + (index * 2)) : 0;
                }

            // Sorted ranges of glyphs, each with its class.
            case 2:
                {
                    var low = 0;
                    var high = FontBinary.UInt16(table, offset + 2) - 1;
                    while (low <= high)
                    {
                        var middle = (low + high) >>> 1;
                        var record = offset + 4 + (middle * 6);
                        if (glyph < FontBinary.UInt16(table, record))
                        {
                            high = middle - 1;
                        }
                        else if (glyph > FontBinary.UInt16(table, record + 2))
                        {
                            low = middle + 1;
                        }
                        else
                        {
                            return FontBinary.UInt16(table, record + 4);
                        }
                    }

                    return 0;
                }

            default:
                return 0;
        }
    }
}
