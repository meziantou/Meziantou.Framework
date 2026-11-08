namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>
/// Writes the tables of Apple Advanced Typography from their description: Apple TrueType Reference Manual, "The 'morx'
/// table", "The 'kerx' table", "The 'feat' table", "The 'trak' table", "The 'ankr' table", and the lookup tables and
/// extended state tables of "Font Tables".
/// </summary>
internal static class FontAatWriter
{
    /// <summary>Writes a lookup table of 16-bit values in one of the formats 0, 2, 4, 6, 8 and 10.</summary>
    /// <param name="values">The value of each glyph that has one.</param>
    /// <param name="format">The format.</param>
    /// <param name="glyphCount">The number of glyphs of the font, for the format 0.</param>
    public static byte[] WriteLookup(IReadOnlyDictionary<int, int> values, int format = 6, int glyphCount = 0)
    {
        var sorted = values.OrderBy(pair => pair.Key).ToList();
        var writer = new FontWriter();
        writer.UInt16(format);
        switch (format)
        {
            case 0:
                for (var glyph = 0; glyph < glyphCount; glyph++)
                {
                    writer.UInt16(values.TryGetValue(glyph, out var value) ? value : 0);
                }

                break;

            case 2:
                {
                    // Runs of glyphs that follow each other with one value.
                    var segments = new List<(int Last, int First, int Value)>();
                    foreach (var (glyph, value) in sorted)
                    {
                        if (segments.Count > 0 && segments[^1].Last == glyph - 1 && segments[^1].Value == value)
                        {
                            segments[^1] = (glyph, segments[^1].First, value);
                        }
                        else
                        {
                            segments.Add((glyph, glyph, value));
                        }
                    }

                    WriteBinarySearchHeader(writer, 6, segments.Count + 1);
                    foreach (var (last, first, value) in segments)
                    {
                        writer.UInt16(last);
                        writer.UInt16(first);
                        writer.UInt16(value);
                    }

                    writer.UInt16(0xFFFF);
                    writer.UInt16(0xFFFF);
                    writer.UInt16(0);
                    break;
                }

            case 4:
                {
                    // Runs of glyphs that follow each other, each with an array of values after the segments.
                    var segments = new List<List<(int Glyph, int Value)>>();
                    foreach (var pair in sorted)
                    {
                        if (segments.Count > 0 && segments[^1][^1].Glyph == pair.Key - 1)
                        {
                            segments[^1].Add((pair.Key, pair.Value));
                        }
                        else
                        {
                            segments.Add([(pair.Key, pair.Value)]);
                        }
                    }

                    WriteBinarySearchHeader(writer, 6, segments.Count + 1);
                    var arrays = 12 + ((segments.Count + 1) * 6);
                    foreach (var segment in segments)
                    {
                        writer.UInt16(segment[^1].Glyph);
                        writer.UInt16(segment[0].Glyph);
                        writer.UInt16(arrays);
                        arrays += segment.Count * 2;
                    }

                    writer.UInt16(0xFFFF);
                    writer.UInt16(0xFFFF);
                    writer.UInt16(0);
                    foreach (var (_, value) in segments.SelectMany(segment => segment))
                    {
                        writer.UInt16(value);
                    }

                    break;
                }

            case 6:
                WriteBinarySearchHeader(writer, 4, sorted.Count + 1);
                foreach (var (glyph, value) in sorted)
                {
                    writer.UInt16(glyph);
                    writer.UInt16(value);
                }

                writer.UInt16(0xFFFF);
                writer.UInt16(0);
                break;

            case 8 or 10:
                {
                    var first = sorted.Count == 0 ? 0 : sorted[0].Key;
                    var count = sorted.Count == 0 ? 0 : sorted[^1].Key - first + 1;
                    if (format == 10)
                    {
                        writer.UInt16(2);
                    }

                    writer.UInt16(first);
                    writer.UInt16(count);
                    for (var glyph = first; glyph < first + count; glyph++)
                    {
                        writer.UInt16(values.TryGetValue(glyph, out var value) ? value : 0);
                    }

                    break;
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown lookup table format");
        }

        return writer.ToArray();
    }

    private static void WriteBinarySearchHeader(FontWriter writer, int unitSize, int units)
    {
        var power = 1;
        var selector = 0;
        while (power * 2 <= units)
        {
            power *= 2;
            selector++;
        }

        writer.UInt16(unitSize);
        writer.UInt16(units);
        writer.UInt16(unitSize * power);
        writer.UInt16(selector);
        writer.UInt16(unitSize * (units - power));
    }

    /// <summary>
    /// Writes an extended state table followed by the tables of the table that has it: the header (number of classes,
    /// offsets of the class table, of the state array and of the entry table), one 32-bit value per extra table, then
    /// the class table, the state array, the entries and the extra tables, each aligned on 4 bytes.
    /// </summary>
    /// <param name="classes">The class of each glyph that has one (4 and more).</param>
    /// <param name="classCount">The number of classes, the 4 predefined ones included.</param>
    /// <param name="states">For each state, the index of the entry of each class.</param>
    /// <param name="entries">The entries, each as its 16-bit values (the next state first).</param>
    /// <param name="extraTables">The tables whose offsets from the start of the state table follow the header.</param>
    /// <param name="encodeExtra">Turns the offset of an extra table into the value of the header, or <see langword="null"/> to write the offset.</param>
    /// <param name="classFormat">The format of the lookup table of the classes.</param>
    public static byte[] WriteStateTable(IReadOnlyDictionary<int, int> classes, int classCount, int[][] states, int[][] entries, IReadOnlyList<byte[]> extraTables, Func<int, uint, uint>? encodeExtra = null, int classFormat = 8)
    {
        var classTable = WriteLookup(classes, classFormat);
        var headerLength = 16 + (extraTables.Count * 4);
        var classOffset = headerLength;
        var stateOffset = Align(classOffset + classTable.Length);
        var entryOffset = Align(stateOffset + (states.Length * classCount * 2));
        var extraOffsets = new List<int>();
        var offset = Align(entryOffset + entries.Sum(entry => entry.Length * 2));
        foreach (var extra in extraTables)
        {
            extraOffsets.Add(offset);
            offset = Align(offset + extra.Length);
        }

        var writer = new FontWriter();
        writer.UInt32((uint)classCount);
        writer.UInt32((uint)classOffset);
        writer.UInt32((uint)stateOffset);
        writer.UInt32((uint)entryOffset);
        for (var i = 0; i < extraOffsets.Count; i++)
        {
            writer.UInt32(encodeExtra?.Invoke(i, (uint)extraOffsets[i]) ?? (uint)extraOffsets[i]);
        }

        writer.Bytes(classTable);
        writer.Pad(4);
        foreach (var state in states)
        {
            if (state.Length != classCount)
                throw new ArgumentException("Every state has an entry per class", nameof(states));

            foreach (var entry in state)
            {
                writer.UInt16(entry);
            }
        }

        writer.Pad(4);
        foreach (var value in entries.SelectMany(entry => entry))
        {
            writer.UInt16(value);
        }

        foreach (var extra in extraTables)
        {
            writer.Pad(4);
            writer.Bytes(extra);
        }

        writer.Pad(4);
        return writer.ToArray();
    }

    private static int Align(int offset) => (offset + 3) & ~3;

    /// <summary>Writes the lookup tables of a contextual subtable: the offsets of the tables, then the tables.</summary>
    public static byte[] WriteSubstitutionTables(IReadOnlyList<IReadOnlyDictionary<int, int>> tables, int format = 6)
    {
        var lookups = tables.Select(table => WriteLookup(table, format)).ToList();
        var writer = new FontWriter();
        var offset = lookups.Count * 4;
        foreach (var lookup in lookups)
        {
            writer.UInt32((uint)offset);
            offset = Align(offset + lookup.Length);
        }

        foreach (var lookup in lookups)
        {
            writer.Bytes(lookup);
            writer.Pad(4);
        }

        return writer.ToArray();
    }

    /// <summary>Writes an array of 16-bit values.</summary>
    public static byte[] WriteUInt16Array(IEnumerable<int> values)
    {
        var writer = new FontWriter();
        foreach (var value in values)
        {
            writer.UInt16(value & 0xFFFF);
        }

        return writer.ToArray();
    }

    /// <summary>Writes an array of 32-bit values.</summary>
    public static byte[] WriteUInt32Array(IEnumerable<uint> values)
    {
        var writer = new FontWriter();
        foreach (var value in values)
        {
            writer.UInt32(value);
        }

        return writer.ToArray();
    }

    /// <summary>Writes a <c>morx</c> table.</summary>
    /// <param name="chains">The chains.</param>
    /// <param name="version">The version: 2, or 3 (with an empty coverage table after the subtables of each chain).</param>
    public static byte[] WriteMorx(IReadOnlyList<FontMorxChain> chains, int version = 2)
    {
        var writer = new FontWriter();
        writer.UInt16(version);
        writer.UInt16(0);
        writer.UInt32((uint)chains.Count);
        foreach (var chain in chains)
        {
            var body = new FontWriter();
            foreach (var (type, setting, enable, disable) in chain.Features)
            {
                body.UInt16(type);
                body.UInt16(setting);
                body.UInt32(enable);
                body.UInt32(disable);
            }

            foreach (var subtable in chain.Subtables)
            {
                var content = subtable.Content;
                body.UInt32((uint)Align(12 + content.Length));
                body.UInt32(subtable.Coverage);
                body.UInt32(subtable.Flags);
                body.Bytes(content);
                body.Pad(4);
            }

            if (version >= 3)
            {
                // A coverage table that has no bit field for any subtable.
                for (var i = 0; i < chain.Subtables.Count; i++)
                {
                    body.UInt32(0);
                }
            }

            writer.UInt32(chain.DefaultFlags);
            writer.UInt32((uint)(16 + body.Length));
            writer.UInt32((uint)chain.Features.Count);
            writer.UInt32((uint)chain.Subtables.Count);
            writer.Bytes(body.ToArray());
        }

        return writer.ToArray();
    }

    /// <summary>Writes a <c>kerx</c> table.</summary>
    /// <param name="subtables">The subtables: their coverage (orientation, direction and format) and their content after the header.</param>
    /// <param name="version">The version: 2 or 3.</param>
    public static byte[] WriteKerx(IReadOnlyList<(uint Coverage, byte[] Content)> subtables, int version = 2)
    {
        var writer = new FontWriter();
        writer.UInt16(version);
        writer.UInt16(0);
        writer.UInt32((uint)subtables.Count);
        foreach (var (coverage, content) in subtables)
        {
            writer.UInt32((uint)Align(12 + content.Length));
            writer.UInt32(coverage);
            writer.UInt32(0);
            writer.Bytes(content);
            writer.Pad(4);
        }

        if (version >= 3)
        {
            foreach (var _ in subtables)
            {
                writer.UInt32(0xFFFFFFFF);
            }
        }

        return writer.ToArray();
    }

    /// <summary>Writes the content of a <c>kerx</c> subtable of format 0: the pairs and their values.</summary>
    public static byte[] WriteKerxPairs(IReadOnlyDictionary<(int Left, int Right), int> pairs)
    {
        var writer = new FontWriter();
        var power = 1;
        var selector = 0;
        while (power * 2 <= pairs.Count)
        {
            power *= 2;
            selector++;
        }

        writer.UInt32((uint)pairs.Count);
        writer.UInt32((uint)(power * 6));
        writer.UInt32((uint)selector);
        writer.UInt32((uint)((pairs.Count - power) * 6));
        foreach (var ((left, right), value) in pairs.OrderBy(pair => pair.Key))
        {
            writer.UInt16(left);
            writer.UInt16(right);
            writer.Int16(value);
        }

        return writer.ToArray();
    }

    /// <summary>
    /// Writes the content of a <c>kerx</c> subtable of format 2: the classes of the left and right glyphs, and an array
    /// of values with a row per left class and a column per right class (class 0 has no kerning).
    /// </summary>
    public static byte[] WriteKerxClassArray(IReadOnlyDictionary<int, int> leftClasses, IReadOnlyDictionary<int, int> rightClasses, int[][] values)
    {
        const int Header = 12 + 16;

        var columns = values[0].Length;
        var rowWidth = columns * 2;
        var arrayOffset = Header;
        var array = values.SelectMany(row => row).ToList();
        var leftOffset = Align(arrayOffset + (array.Count * 2));

        // The left values are the offsets of the rows from the start of the array; the right ones are offsets in a row.
        var left = WriteLookup(leftClasses.ToDictionary(pair => pair.Key, pair => pair.Value * rowWidth), 8);
        var rightOffset = Align(leftOffset + left.Length);
        var right = WriteLookup(rightClasses.ToDictionary(pair => pair.Key, pair => pair.Value * 2), 8);
        var writer = new FontWriter();
        writer.UInt32((uint)rowWidth);
        writer.UInt32((uint)leftOffset);
        writer.UInt32((uint)rightOffset);
        writer.UInt32((uint)arrayOffset);
        foreach (var value in array)
        {
            writer.Int16(value);
        }

        writer.Pad(4);
        writer.Bytes(left);
        writer.Pad(4);
        writer.Bytes(right);
        return writer.ToArray();
    }

    /// <summary>Writes the content of a <c>kerx</c> subtable of format 6 with values of 16 bits: an index-based array of values.</summary>
    public static byte[] WriteKerxIndexArray(IReadOnlyDictionary<int, int> rows, IReadOnlyDictionary<int, int> columns, int[][] values)
    {
        const int Header = 12 + 20;

        var columnCount = values[0].Length;
        var array = values.SelectMany(row => row).ToList();
        var rowTable = WriteLookup(rows.ToDictionary(pair => pair.Key, pair => pair.Value * columnCount), 8);
        var columnTable = WriteLookup(columns, 8);
        var rowOffset = Header;
        var columnOffset = Align(rowOffset + rowTable.Length);
        var arrayOffset = Align(columnOffset + columnTable.Length);
        var writer = new FontWriter();
        writer.UInt32(0);
        writer.UInt16(values.Length);
        writer.UInt16(columnCount);
        writer.UInt32((uint)rowOffset);
        writer.UInt32((uint)columnOffset);
        writer.UInt32((uint)arrayOffset);
        writer.Bytes(rowTable);
        writer.Pad(4);
        writer.Bytes(columnTable);
        writer.Pad(4);
        foreach (var value in array)
        {
            writer.Int16(value);
        }

        return writer.ToArray();
    }

    /// <summary>Writes a <c>feat</c> table.</summary>
    /// <param name="features">Each feature type, whether its settings exclude each other, the index of its default setting, and its settings.</param>
    public static byte[] WriteFeat(IReadOnlyList<(int Type, bool IsExclusive, int DefaultIndex, int[] Settings)> features)
    {
        var writer = new FontWriter();
        writer.UInt32(0x00010000);
        writer.UInt16(features.Count);
        writer.UInt16(0);
        writer.UInt32(0);
        var settingsOffset = 12 + (features.Count * 12);
        var nameIndex = 256;
        foreach (var (type, isExclusive, defaultIndex, settings) in features.OrderBy(feature => feature.Type))
        {
            writer.UInt16(type);
            writer.UInt16(settings.Length);
            writer.UInt32((uint)settingsOffset);
            writer.UInt16((isExclusive ? 0x8000 : 0) | (defaultIndex != 0 ? 0x4000 | defaultIndex : 0));
            writer.Int16(nameIndex++);
            settingsOffset += settings.Length * 4;
        }

        foreach (var (_, _, _, settings) in features.OrderBy(feature => feature.Type))
        {
            foreach (var setting in settings)
            {
                writer.UInt16(setting);
                writer.Int16(nameIndex++);
            }
        }

        return writer.ToArray();
    }

    /// <summary>Writes a <c>trak</c> table with horizontal tracks.</summary>
    /// <param name="sizes">The sizes, in points, in increasing order.</param>
    /// <param name="tracks">Each track and its value at each size, in font units.</param>
    public static byte[] WriteTrak(IReadOnlyList<double> sizes, IReadOnlyList<(double Track, int[] Values)> tracks)
    {
        const int TrackData = 12;

        var entries = TrackData + 8;
        var sizeTable = entries + (tracks.Count * 8);
        var values = sizeTable + (sizes.Count * 4);
        var writer = new FontWriter();
        writer.UInt32(0x00010000);
        writer.UInt16(0);
        writer.UInt16(TrackData);
        writer.UInt16(0);
        writer.UInt16(0);
        writer.UInt16(tracks.Count);
        writer.UInt16(sizes.Count);
        writer.UInt32((uint)sizeTable);
        var nameIndex = 256;
        foreach (var (track, _) in tracks.OrderBy(track => track.Track))
        {
            writer.Int32((int)Math.Round(track * 65536, MidpointRounding.AwayFromZero));
            writer.UInt16(nameIndex++);
            writer.UInt16(values);
            values += sizes.Count * 2;
        }

        foreach (var size in sizes)
        {
            writer.Int32((int)Math.Round(size * 65536, MidpointRounding.AwayFromZero));
        }

        foreach (var (_, trackValues) in tracks.OrderBy(track => track.Track))
        {
            foreach (var value in trackValues)
            {
                writer.Int16(value);
            }
        }

        return writer.ToArray();
    }

    /// <summary>Writes an <c>ankr</c> table: the anchor points of each glyph that has some.</summary>
    public static byte[] WriteAnkr(IReadOnlyDictionary<int, (int X, int Y)[]> anchors)
    {
        var data = new FontWriter();
        var offsets = new Dictionary<int, int>();
        foreach (var (glyph, points) in anchors.OrderBy(pair => pair.Key))
        {
            offsets[glyph] = data.Length;
            data.UInt32((uint)points.Length);
            foreach (var (x, y) in points)
            {
                data.Int16(x);
                data.Int16(y);
            }
        }

        var lookup = WriteLookup(offsets, 6);
        var writer = new FontWriter();
        writer.UInt16(0);
        writer.UInt16(0);
        writer.UInt32(12);
        writer.UInt32((uint)Align(12 + lookup.Length));
        writer.Bytes(lookup);
        writer.Pad(4);
        writer.Bytes(data.ToArray());
        return writer.ToArray();
    }
}
