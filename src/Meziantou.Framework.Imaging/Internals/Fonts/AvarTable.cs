namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Applies the <c>avar</c> table: the mapping of the normalized value of each axis that a font can add to the default one.</summary>
/// <remarks>
/// OpenType specification, "avar - Axis Variations Table". Each axis has a list of pairs (from, to), sorted by their
/// first value; a value between two pairs is interpolated between them. Version 2 of the table, which adds a mapping
/// that depends on several axes, is not applied: its version 1 part is.
/// </remarks>
internal static class AvarTable
{
    /// <summary>
    /// Maps the normalized coordinates of an instance, in place. The coordinates are 16.16 fixed-point numbers: they are
    /// mapped with the precision they were normalized with, before they are rounded to the 2.14 numbers of the tables.
    /// </summary>
    public static void Map(ReadOnlySpan<byte> table, Span<int> coordinates)
    {
        if (table.Length < 8 || FontBinary.UInt16(table, 0) is not (1 or 2))
            return;

        var axisCount = FontBinary.UInt16(table, 6);
        var position = 8;
        for (var axis = 0; axis < axisCount; axis++)
        {
            var count = FontBinary.UInt16(table, position);
            var maps = FontBinary.Slice(table, position + 2, (long)count * 4);
            position += 2 + (count * 4);
            if (axis < coordinates.Length && count != 0)
            {
                coordinates[axis] = MapValue(maps, count, coordinates[axis]);
            }
        }
    }

    private static int MapValue(ReadOnlySpan<byte> maps, int count, int value)
    {
        // Outside the pairs, the first and the last ones apply; a valid table maps -1, 0 and 1 to themselves.
        // The pairs are 2.14 numbers.
        var previousFrom = FontBinary.Int16(maps, 0) << FontInstance.TableShift;
        var previousTo = FontBinary.Int16(maps, 2) << FontInstance.TableShift;
        if (value <= previousFrom)
            return previousTo;

        for (var i = 1; i < count; i++)
        {
            var from = FontBinary.Int16(maps, i * 4) << FontInstance.TableShift;
            var to = FontBinary.Int16(maps, (i * 4) + 2) << FontInstance.TableShift;
            if (value == from)
                return to;

            if (value < from)
            {
                // from > previousFrom here: the value is strictly between them.
                var numerator = (long)(to - previousTo) * (value - previousFrom);
                return previousTo + (int)Math.Floor(((double)numerator / (from - previousFrom)) + 0.5);
            }

            previousFrom = from;
            previousTo = to;
        }

        return previousTo;
    }
}
