namespace Meziantou.Framework.Fonts;

/// <summary>Reads an item variation store: sets of deltas, each for a region of the variation space.</summary>
/// <remarks>
/// <para>
/// OpenType specification, "OpenType Font Variations Common Table Formats", "Item variation store". A region gives, for
/// each axis, the coordinates where its deltas start to apply, apply fully and stop applying; the scalar of a region
/// for an instance is the product of what each axis gives. The value of an item for an instance is its default value
/// plus the sum of its deltas, each multiplied by the scalar of its region.
/// </para>
/// <para>
/// The scalars only depend on the instance: they are computed once for it (<see cref="ComputeScalars"/>), and reading
/// a delta is then a sum over the regions of the item.
/// </para>
/// </remarks>
internal static class ItemVariationStore
{
    private const int LongWords = 0x8000;

    /// <summary>Computes the scalar of each region of a store for the normalized coordinates of an instance.</summary>
    /// <param name="table">The table that contains the store.</param>
    /// <param name="store">The position of the store in the table.</param>
    /// <param name="coordinates">The normalized coordinates, as 2.14 fixed-point numbers. An axis the instance does not have is at its default.</param>
    public static double[] ComputeScalars(ReadOnlySpan<byte> table, int store, ReadOnlySpan<int> coordinates)
    {
        if (FontBinary.UInt16(table, store) != 1)
            return [];

        var regions = FontBinary.AddOffset(store, FontBinary.Offset32(table, store + 2));
        var axisCount = FontBinary.UInt16(table, regions);
        var regionCount = FontBinary.UInt16(table, regions + 2);
        var records = FontBinary.Slice(table, regions + 4, (long)regionCount * axisCount * 6);
        var scalars = new double[regionCount];
        for (var region = 0; region < scalars.Length; region++)
        {
            var scalar = 1.0;
            var record = records.Slice(region * axisCount * 6, axisCount * 6);
            for (var axis = 0; axis < axisCount && scalar != 0; axis++)
            {
                var start = FontBinary.Int16(record, axis * 6);
                var peak = FontBinary.Int16(record, (axis * 6) + 2);
                var end = FontBinary.Int16(record, (axis * 6) + 4);
                scalar *= GetAxisScalar(start, peak, end, axis < coordinates.Length ? coordinates[axis] : 0);
            }

            scalars[region] = scalar;
        }

        return scalars;
    }

    /// <summary>Gets what an axis contributes to the scalar of a region: 1 at the peak, 0 outside the region, a line between them.</summary>
    public static double GetAxisScalar(int start, int peak, int end, int coordinate)
    {
        // An axis that does not take part in the region, or whose values are not ordered or cross zero, is ignored.
        if (peak == 0 || start > peak || peak > end || (start < 0 && end > 0))
            return 1;

        if (coordinate == peak)
            return 1;

        if (coordinate <= start || coordinate >= end)
            return 0;

        return coordinate < peak
            ? (double)(coordinate - start) / (peak - start)
            : (double)(end - coordinate) / (end - peak);
    }

    /// <summary>Gets the number of regions the items of a subtable of the store have deltas for.</summary>
    public static int GetRegionCount(ReadOnlySpan<byte> table, int store, int outer)
    {
        if (FontBinary.UInt16(table, store) != 1 || outer >= FontBinary.UInt16(table, store + 6))
            return 0;

        var data = FontBinary.AddOffset(store, FontBinary.Offset32(table, store + 8 + (outer * 4)));
        return FontBinary.UInt16(table, data + 4);
    }

    /// <summary>Gets the scalar of the regions of a subtable of the store, in the order of the deltas of its items.</summary>
    /// <returns>The number of regions.</returns>
    public static int GetRegionScalars(ReadOnlySpan<byte> table, int store, int outer, ReadOnlySpan<double> scalars, Span<double> destination)
    {
        var count = GetRegionCount(table, store, outer);
        if (count == 0)
            return 0;

        if (count > destination.Length)
            throw FontErrors.Invalid("a variation store has more regions for an item than the font format allows.");

        var data = FontBinary.AddOffset(store, FontBinary.Offset32(table, store + 8 + (outer * 4)));
        for (var i = 0; i < count; i++)
        {
            var region = FontBinary.UInt16(table, data + 6 + (i * 2));
            destination[i] = region < scalars.Length ? scalars[region] : 0;
        }

        return count;
    }

    /// <summary>Gets the delta of an item for an instance.</summary>
    /// <param name="table">The table that contains the store.</param>
    /// <param name="store">The position of the store in the table.</param>
    /// <param name="outer">The index of the subtable of the item.</param>
    /// <param name="inner">The index of the item in its subtable.</param>
    /// <param name="scalars">The scalars of the regions of the store for the instance.</param>
    /// <returns>The delta, which is not rounded; 0 for an item the store does not have.</returns>
    public static double GetDelta(ReadOnlySpan<byte> table, int store, int outer, int inner, ReadOnlySpan<double> scalars)
    {
        if (scalars.IsEmpty || outer >= FontBinary.UInt16(table, store + 6))
            return 0;

        var data = FontBinary.AddOffset(store, FontBinary.Offset32(table, store + 8 + (outer * 4)));
        var itemCount = FontBinary.UInt16(table, data);
        if (inner >= itemCount)
            return 0;

        var wordCount = FontBinary.UInt16(table, data + 2);
        var regionCount = FontBinary.UInt16(table, data + 4);
        var isLong = (wordCount & LongWords) != 0;
        wordCount &= ~LongWords;
        if (wordCount > regionCount)
            throw FontErrors.Invalid("a variation store has more wide deltas than regions.");

        // A row: the wide deltas, then the narrow ones. Wide and narrow are 32 and 16 bits, or 16 and 8 bits.
        var wide = isLong ? 4 : 2;
        var narrow = isLong ? 2 : 1;
        var rowLength = (wordCount * wide) + ((regionCount - wordCount) * narrow);
        var regions = FontBinary.Slice(table, data + 6, regionCount * 2);
        var row = FontBinary.Slice(table, (long)data + 6 + (regionCount * 2) + ((long)inner * rowLength), rowLength);
        var delta = 0.0;
        var position = 0;
        for (var i = 0; i < regionCount; i++)
        {
            int value;
            if (i < wordCount)
            {
                value = isLong ? FontBinary.Int32(row, position) : FontBinary.Int16(row, position);
                position += wide;
            }
            else
            {
                value = isLong ? FontBinary.Int16(row, position) : FontBinary.Int8(row, position);
                position += narrow;
            }

            var region = FontBinary.UInt16(regions, i * 2);
            if (value != 0 && region < scalars.Length)
            {
                delta += scalars[region] * value;
            }
        }

        return delta;
    }

    /// <summary>
    /// Reads an entry of a delta set index map: the subtable and the item, in a store, of the deltas of a glyph or of
    /// another numbered thing.
    /// </summary>
    /// <remarks>
    /// "Associating target items to variation data". An entry packs both indices in one to four bytes; an index beyond
    /// the map uses its last entry.
    /// </remarks>
    public static void ReadIndexMap(ReadOnlySpan<byte> table, int map, int index, out int outer, out int inner)
    {
        var format = FontBinary.UInt8(table, map);
        var entryFormat = FontBinary.UInt8(table, map + 1);
        long count;
        int entries;
        switch (format)
        {
            case 0:
                count = FontBinary.UInt16(table, map + 2);
                entries = map + 4;
                break;

            case 1:
                count = FontBinary.UInt32(table, map + 2);
                entries = map + 6;
                break;

            default:
                throw FontErrors.Invalid($"a delta set index map has the unknown format {format}.");
        }

        if (count == 0)
        {
            outer = index >> 16;
            inner = index & 0xFFFF;
            return;
        }

        var entrySize = ((entryFormat & 0x30) >> 4) + 1;
        var innerBits = (entryFormat & 0x0F) + 1;
        var entry = FontBinary.Slice(table, entries + (Math.Min(index, count - 1) * entrySize), entrySize);
        uint value = 0;
        foreach (var part in entry)
        {
            value = (value << 8) | part;
        }

        outer = (int)(value >> innerBits);
        inner = (int)(value & ((1u << innerBits) - 1));
    }

    /// <summary>Rounds a delta of a metric to font units: to the nearest, halves upward.</summary>
    public static int Round(double delta) => (int)Math.Floor(delta + 0.5);
}
