namespace Meziantou.Framework.Fonts;

/// <summary>
/// The lookup tables of the AAT tables: Apple TrueType Reference Manual, "Font Tables", "Lookup Tables". A lookup
/// table gives a value to some glyphs; the other glyphs have none.
/// </summary>
/// <remarks>
/// <para>
/// Formats 0 (an array indexed by glyph), 2 (segments with one value), 4 (segments with an array of values), 6
/// (sorted pairs), 8 (a trimmed array) and 10 (a trimmed array whose values have 1, 2, 4 or 8 bytes) are read. The
/// binary search formats may end with a segment or a pair of glyph 0xFFFF, which is not a glyph.
/// </para>
/// <para>
/// Every read is checked against the bounds of the table (<see cref="FontBinary"/>): a lookup table that points
/// outside it is an <see cref="InvalidFontException"/>.
/// </para>
/// </remarks>
internal static class AatLookup
{
    /// <summary>The glyph of the binary search tables that ends them, which is no glyph.</summary>
    private const int EndGlyph = 0xFFFF;

    /// <summary>Checks the header of a lookup table, so that a lookup of a glyph reads in the table only.</summary>
    /// <exception cref="InvalidFontException">The header is malformed or the format is unknown.</exception>
    public static void Validate(ReadOnlySpan<byte> table, int glyphCount, int valueSize = 2)
    {
        var format = FontBinary.UInt16(table, 0);
        switch (format)
        {
            case 0:
                FontBinary.EnsureRange(table, 2, (long)glyphCount * valueSize);
                break;

            case 2 or 4 or 6:
                var unitSize = FontBinary.UInt16(table, 2);
                var units = FontBinary.UInt16(table, 4);
                if (unitSize < (format == 6 ? 2 + valueSize : 6))
                    throw FontErrors.Invalid("a lookup table of an AAT table has units that are too small.");

                FontBinary.EnsureRange(table, 12, (long)unitSize * units);
                break;

            case 8:
                FontBinary.EnsureRange(table, 6, (long)FontBinary.UInt16(table, 4) * valueSize);
                break;

            case 10:
                var size = FontBinary.UInt16(table, 2);
                if (size is not (1 or 2 or 4 or 8))
                    throw FontErrors.Invalid("a lookup table of an AAT table has values of an unknown size.");

                FontBinary.EnsureRange(table, 8, (long)FontBinary.UInt16(table, 6) * size);
                break;

            default:
                throw FontErrors.Invalid($"a lookup table of an AAT table has the unknown format {format}.");
        }
    }

    /// <summary>Gets the 16-bit value of a glyph.</summary>
    /// <param name="table">The lookup table, from its format.</param>
    /// <param name="glyph">The glyph.</param>
    /// <param name="glyphCount">The number of glyphs of the font, which is the length of a table of format 0.</param>
    /// <param name="value">The value of the glyph.</param>
    /// <returns><see langword="true"/> when the table has a value for the glyph.</returns>
    /// <exception cref="InvalidFontException">The table is malformed.</exception>
    public static bool TryGetValue(ReadOnlySpan<byte> table, int glyph, int glyphCount, out int value)
    {
        var result = TryGetValue(table, glyph, glyphCount, valueSize: 2, out var wide);
        value = (int)wide;
        return result;
    }

    /// <summary>Gets the value of a glyph, of a size in bytes the table that has the lookup table defines.</summary>
    /// <exception cref="InvalidFontException">The table is malformed.</exception>
    public static bool TryGetValue(ReadOnlySpan<byte> table, int glyph, int glyphCount, int valueSize, out ulong value)
    {
        value = 0;
        if ((uint)glyph >= EndGlyph)
            return false;

        var format = FontBinary.UInt16(table, 0);
        switch (format)
        {
            case 0:
                if (glyph >= glyphCount)
                    return false;

                value = ReadValue(table, 2 + (glyph * valueSize), valueSize);
                return true;

            case 2:
                {
                    var unit = FindSegment(table, glyph);
                    if (unit < 0)
                        return false;

                    value = ReadValue(table, unit + 4, valueSize);
                    return true;
                }

            case 4:
                {
                    var unit = FindSegment(table, glyph);
                    if (unit < 0)
                        return false;

                    // The value of the segment is the offset, from the start of the lookup table, of the values of its glyphs.
                    var first = FontBinary.UInt16(table, unit + 2);
                    value = ReadValue(table, FontBinary.UInt16(table, unit + 4) + ((glyph - first) * valueSize), valueSize);
                    return true;
                }

            case 6:
                {
                    var unitSize = FontBinary.UInt16(table, 2);
                    var units = FontBinary.UInt16(table, 4);
                    int low = 0, high = units - 1;
                    while (low <= high)
                    {
                        var middle = (low + high) >> 1;
                        var position = 12 + (middle * unitSize);
                        var current = FontBinary.UInt16(table, position);
                        if (current == glyph)
                        {
                            value = ReadValue(table, position + 2, valueSize);
                            return true;
                        }

                        if (current < glyph)
                        {
                            low = middle + 1;
                        }
                        else
                        {
                            high = middle - 1;
                        }
                    }

                    return false;
                }

            case 8:
                {
                    var first = FontBinary.UInt16(table, 2);
                    var count = FontBinary.UInt16(table, 4);
                    if (glyph < first || glyph >= first + count)
                        return false;

                    value = ReadValue(table, 6 + ((glyph - first) * valueSize), valueSize);
                    return true;
                }

            case 10:
                {
                    var size = FontBinary.UInt16(table, 2);
                    var first = FontBinary.UInt16(table, 4);
                    var count = FontBinary.UInt16(table, 6);
                    if (glyph < first || glyph >= first + count)
                        return false;

                    value = ReadValue(table, 8 + ((glyph - first) * size), size);
                    return true;
                }

            default:
                throw FontErrors.Invalid($"a lookup table of an AAT table has the unknown format {format}.");
        }
    }

    /// <summary>Finds the segment of a glyph in a table of format 2 or 4, sorted by the last glyph of each segment.</summary>
    /// <returns>The position of the segment in the table, or -1.</returns>
    private static int FindSegment(ReadOnlySpan<byte> table, int glyph)
    {
        var unitSize = FontBinary.UInt16(table, 2);
        var units = FontBinary.UInt16(table, 4);
        int low = 0, high = units - 1;
        while (low <= high)
        {
            var middle = (low + high) >> 1;
            var position = 12 + (middle * unitSize);
            var last = FontBinary.UInt16(table, position);
            var first = FontBinary.UInt16(table, position + 2);
            if (last != EndGlyph && glyph >= first && glyph <= last)
                return position;

            if (last == EndGlyph || glyph < first)
            {
                high = middle - 1;
            }
            else
            {
                low = middle + 1;
            }
        }

        return -1;
    }

    private static ulong ReadValue(ReadOnlySpan<byte> table, int position, int size) => size switch
    {
        1 => (ulong)FontBinary.UInt8(table, position),
        2 => (ulong)FontBinary.UInt16(table, position),
        4 => FontBinary.UInt32(table, position),
        _ => ((ulong)FontBinary.UInt32(table, position) << 32) | FontBinary.UInt32(table, position + 4),
    };
}
