namespace Meziantou.Framework.Fonts;

/// <summary>
/// The glyphs each lookup of a GSUB or GPOS table, and each subtable of a lookup, may apply at, as a digest: a lookup
/// or a subtable is tried at a glyph only when the digest of its first coverage tables may contain it.
/// </summary>
/// <remarks>
/// <para>
/// Every subtable of every lookup type starts by looking its current glyph up in one coverage table (the first glyph
/// of a sequence, a mark, the glyph a pair starts with), and does nothing when the glyph is not covered: the union of
/// these coverage tables is a superset of the glyphs a lookup applies at. The digest of a set is three masks of 64
/// bits, each with a bit for a part of the glyph index; a glyph whose three bits are not all set is not in the set.
/// </para>
/// <para>
/// Nothing is skipped that could have thrown: the digest of a subtable whose first coverage table cannot be read
/// completely (a reference outside the table) contains every glyph, so that the lookup is tried, and fails, as before.
/// What a subtable reads before it looks the glyph up is read here as well.
/// </para>
/// </remarks>
internal sealed class LookupDigests
{
    private const int SubstitutionContext = 5;
    private const int SubstitutionChainedContext = 6;
    private const int SubstitutionExtension = 7;
    private const int PositioningCursive = 3;
    private const int PositioningContext = 7;
    private const int PositioningChainedContext = 8;
    private const int PositioningExtension = 9;

    private readonly LayoutTable _table;
    private readonly ReadOnlyMemory<byte> _data;
    private readonly bool _isPositioning;
    private GlyphDigest[]? _digests;
    private GlyphDigest[]?[]? _subtableDigests;
    private bool[]? _isComputed;

    public LookupDigests(LayoutTable table, ReadOnlyMemory<byte> data, bool isPositioning)
    {
        _table = table;
        _data = data;
        _isPositioning = isPositioning;
    }

    /// <summary>Gets the digest of a lookup, which <see cref="LayoutTable.GetLookup"/> has read.</summary>
    /// <param name="index">The index of the lookup in the lookup list.</param>
    /// <param name="subtables">The digest of each subtable of the lookup, or <see langword="null"/> when the lookup has one subtable or cannot be read.</param>
    public GlyphDigest Get(int index, out GlyphDigest[]? subtables)
    {
        if (_digests is null)
        {
            var count = _table.GetLookupCount(_data.Span);
            _digests = new GlyphDigest[count];
            _subtableDigests = new GlyphDigest[count][];
            _isComputed = new bool[count];
        }

        if ((uint)index >= (uint)_digests.Length)
        {
            subtables = null;
            return GlyphDigest.All;
        }

        if (!_isComputed![index])
        {
            _digests[index] = Compute(index, out _subtableDigests![index]);
            _isComputed[index] = true;
        }

        subtables = _subtableDigests![index];
        return _digests[index];
    }

    private GlyphDigest Compute(int index, out GlyphDigest[]? subtables)
    {
        var data = _data.Span;
        var lookup = _table.GetLookup(data, index);
        subtables = null;
        if (!TryUInt16(data, lookup.Offset + 6L + ((long)lookup.SubtableCount * 2) - 2, out _) && lookup.SubtableCount > 0)
            return GlyphDigest.All;

        var digest = default(GlyphDigest);
        subtables = lookup.SubtableCount > 1 ? new GlyphDigest[lookup.SubtableCount] : null;
        for (var i = 0; i < lookup.SubtableCount; i++)
        {
            var subtable = default(GlyphDigest);
            if (!AddSubtable(data, lookup.Type, LayoutTable.GetSubtable(data, lookup, i), ref subtable))
            {
                subtable = GlyphDigest.All;
            }

            if (subtables is not null)
            {
                subtables[i] = subtable;
            }

            digest.Add(subtable);
        }

        return digest;
    }

    /// <summary>Adds the glyphs a subtable may apply at.</summary>
    /// <returns><see langword="false"/> when the subtable cannot be read: it is tried at every glyph.</returns>
    private bool AddSubtable(ReadOnlySpan<byte> data, int type, long subtable, ref GlyphDigest digest)
    {
        var extension = _isPositioning ? PositioningExtension : SubstitutionExtension;
        var context = _isPositioning ? PositioningContext : SubstitutionContext;
        var chainedContext = _isPositioning ? PositioningChainedContext : SubstitutionChainedContext;
        if (type == extension)
        {
            // The subtable of another type at a 32-bit offset; a subtable of an unknown format or that refers to an
            // extension applies nowhere.
            if (!TryUInt16(data, subtable, out var extensionFormat) || !TryUInt16(data, subtable + 2, out var extensionType) || !TryUInt32(data, subtable + 4, out var offset))
                return false;

            if (extensionFormat != 1 || extensionType == extension)
                return true;

            if (offset > int.MaxValue || subtable + offset > int.MaxValue)
                return false;

            return AddSubtable(data, extensionType, subtable + offset, ref digest);
        }

        // The second field is the offset of the first coverage table, or a count in the third format of the contexts.
        if (!TryUInt16(data, subtable, out var format) || !TryUInt16(data, subtable + 2, out var field))
            return false;

        if (type == context || type == chainedContext)
        {
            switch (format)
            {
                case 1 or 2:
                    return AddCoverage(data, subtable + field, ref digest);

                case 3 when type == context:
                    // The number of glyphs of the sequence, the number of lookups, then the coverage table of each glyph.
                    if (field == 0)
                        return true;

                    return TryUInt16(data, subtable + 6, out var first) && AddCoverage(data, subtable + first, ref digest);

                case 3:
                    {
                        // The coverage tables of the glyphs before, then the number of glyphs of the sequence and their coverage tables.
                        var input = subtable + 4 + (field * 2L);
                        if (!TryUInt16(data, input, out var inputCount))
                            return false;

                        if (inputCount == 0)
                            return true;

                        return TryUInt16(data, input + 2, out var inputCoverage) && AddCoverage(data, subtable + inputCoverage, ref digest);
                    }

                default:
                    return true;
            }
        }

        if (_isPositioning ? type is < 1 or > 6 : type is < 1 or > 8)
            return true;

        // A cursive attachment of an unknown format applies nowhere; the other types look the glyph up whatever their format.
        if (_isPositioning && type == PositioningCursive && format != 1)
            return true;

        return AddCoverage(data, subtable + field, ref digest);
    }

    /// <summary>Adds the glyphs of a coverage table.</summary>
    private static bool AddCoverage(ReadOnlySpan<byte> data, long offset, ref GlyphDigest digest)
    {
        if (!TryUInt16(data, offset, out var format) || !TryUInt16(data, offset + 2, out var count))
            return false;

        switch (format)
        {
            case 1:
                if (!TryUInt16(data, offset + 4 + (count * 2L) - 2, out _) && count > 0)
                    return false;

                for (var i = 0; i < count; i++)
                {
                    var glyph = (data[(int)offset + 4 + (i * 2)] << 8) | data[(int)offset + 5 + (i * 2)];
                    digest.Add(glyph);
                }

                return true;

            case 2:
                if (!TryUInt16(data, offset + 4 + (count * 6L) - 2, out _) && count > 0)
                    return false;

                for (var i = 0; i < count; i++)
                {
                    var record = (int)offset + 4 + (i * 6);
                    var start = (data[record] << 8) | data[record + 1];
                    var end = (data[record + 2] << 8) | data[record + 3];
                    digest.AddRange(start, end);
                }

                return true;

            default:
                // No glyph is covered.
                return true;
        }
    }

    private static bool TryUInt16(ReadOnlySpan<byte> data, long offset, out int value)
    {
        if (offset < 0 || offset > data.Length - 2)
        {
            value = 0;
            return false;
        }

        value = (data[(int)offset] << 8) | data[(int)offset + 1];
        return true;
    }

    private static bool TryUInt32(ReadOnlySpan<byte> data, long offset, out long value)
    {
        if (offset < 0 || offset > data.Length - 4)
        {
            value = 0;
            return false;
        }

        value = ((long)data[(int)offset] << 24) | ((long)data[(int)offset + 1] << 16) | ((long)data[(int)offset + 2] << 8) | data[(int)offset + 3];
        return true;
    }
}
