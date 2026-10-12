namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Applies the <c>avar</c> table: the mapping of the normalized value of each axis that a font can add to the default one.</summary>
/// <remarks>
/// <para>
/// OpenType specification, "avar - Axis Variations Table". Each axis has a list of pairs (from, to), sorted by their
/// first value; a value between two pairs is interpolated between them.
/// </para>
/// <para>
/// Version 2 of the table ("avar - Axis Variations Table Version 2", the proposal the Open Font Format adopted) adds
/// to the value of each axis a delta that depends on the values of all the axes: an item variation store whose items
/// are the axes, through an optional delta set index map. The deltas are computed from the values the pairs give,
/// all of them before any is changed, rounded to the nearest 2.14 number and added; the result is kept between -1
/// and 1.
/// </para>
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

    /// <summary>
    /// Applies what version 2 of the table adds to the coordinates of an instance, in place: the deltas of its variation
    /// store. The coordinates are the 2.14 numbers that <see cref="Map"/> gives, once rounded.
    /// </summary>
    public static void AddDeltas(ReadOnlySpan<byte> table, Span<int> coordinates)
    {
        if (table.Length < 8 || FontBinary.UInt16(table, 0) != 2)
            return;

        // The pairs of each axis, then the positions of the index map and of the store.
        var axisCount = FontBinary.UInt16(table, 6);
        var position = 8;
        for (var axis = 0; axis < axisCount; axis++)
        {
            position += 2 + (FontBinary.UInt16(table, position) * 4);
        }

        var indexMap = FontBinary.Offset32(table, position);
        var store = FontBinary.Offset32(table, position + 4);
        if (store == 0)
            return;

        var scalars = ItemVariationStore.ComputeScalars(table, store, coordinates);
        Span<int> deltas = coordinates.Length <= 64 ? stackalloc int[coordinates.Length] : new int[coordinates.Length];
        for (var axis = 0; axis < coordinates.Length; axis++)
        {
            // Without index map, the item of an axis is the one of its index in the first subtable.
            var outer = axis >> 16;
            var inner = axis & 0xFFFF;
            if (indexMap != 0)
            {
                ItemVariationStore.ReadIndexMap(table, indexMap, axis, out outer, out inner);
            }

            deltas[axis] = (int)Math.Round(ItemVariationStore.GetDelta(table, store, outer, inner, scalars), MidpointRounding.AwayFromZero);
        }

        for (var axis = 0; axis < coordinates.Length; axis++)
        {
            coordinates[axis] = Math.Clamp(coordinates[axis] + deltas[axis], -FontInstance.TableOne, FontInstance.TableOne);
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
