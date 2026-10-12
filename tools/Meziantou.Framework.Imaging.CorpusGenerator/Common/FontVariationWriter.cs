namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>
/// Writes the tables of a variable font from their description: OpenType specification, "fvar", "avar", "gvar", "HVAR",
/// "MVAR" and "OpenType Font Variations Common Table Formats".
/// </summary>
/// <remarks>The writer has no dependency on the library: the tests compare what the library reads with these descriptions.</remarks>
internal static class FontVariationWriter
{
    /// <summary>Writes the <c>fvar</c> table. The names of the axes and of the instances take identifiers from <paramref name="firstNameId"/>.</summary>
    public static byte[] WriteFvar(IReadOnlyList<FontAxisDefinition> axes, IReadOnlyList<FontNamedInstanceDefinition> instances, int firstNameId, SortedDictionary<int, string> names)
    {
        var hasPostScriptNames = instances.Any(instance => instance.PostScriptName is not null);
        var writer = new FontWriter();
        writer.UInt16(1);
        writer.UInt16(0);
        writer.UInt16(16);
        writer.UInt16(2);
        writer.UInt16(axes.Count);
        writer.UInt16(20);
        writer.UInt16(instances.Count);
        writer.UInt16(4 + (axes.Count * 4) + (hasPostScriptNames ? 2 : 0));
        var nameId = firstNameId;
        foreach (var axis in axes)
        {
            writer.Tag(axis.Tag);
            writer.Int32(ToFixed(axis.Minimum));
            writer.Int32(ToFixed(axis.Default));
            writer.Int32(ToFixed(axis.Maximum));
            writer.UInt16(axis.IsHidden ? 1 : 0);
            writer.UInt16(nameId);
            names[nameId++] = axis.Name;
        }

        foreach (var instance in instances)
        {
            writer.UInt16(nameId);
            names[nameId++] = instance.Name;
            writer.UInt16(0);
            foreach (var coordinate in instance.Coordinates)
            {
                writer.Int32(ToFixed(coordinate));
            }

            if (hasPostScriptNames)
            {
                if (instance.PostScriptName is null)
                {
                    writer.UInt16(0xFFFF);
                }
                else
                {
                    writer.UInt16(nameId);
                    names[nameId++] = instance.PostScriptName;
                }
            }
        }

        return writer.ToArray();
    }

    /// <summary>Writes the <c>avar</c> table: the pairs of each axis.</summary>
    public static byte[] WriteAvar(IReadOnlyList<FontAxisDefinition> axes)
    {
        var writer = new FontWriter();
        writer.UInt16(1);
        writer.UInt16(0);
        writer.UInt16(0);
        writer.UInt16(axes.Count);
        foreach (var axis in axes)
        {
            writer.UInt16(axis.Map.Count);
            foreach (var (from, to) in axis.Map)
            {
                writer.F2Dot14(from);
                writer.F2Dot14(to);
            }
        }

        return writer.ToArray();
    }

    /// <summary>Writes an item variation store.</summary>
    public static byte[] WriteStore(FontVariationStore store, int axisCount)
    {
        var subtables = new List<byte[]>();
        foreach (var subtable in store.Subtables)
        {
            // The columns that need the wide form come first: the caller orders the regions accordingly.
            (int Min, int Max) wide = subtable.UseLongDeltas ? (short.MinValue, short.MaxValue) : (sbyte.MinValue, sbyte.MaxValue);
            var wordCount = 0;
            for (var column = 0; column < subtable.Regions.Length; column++)
            {
                if (subtable.Items.Any(item => item[column] < wide.Min || item[column] > wide.Max))
                {
                    wordCount = column + 1;
                }
            }

            var data = new FontWriter();
            data.UInt16(subtable.Items.Count);
            data.UInt16(wordCount | (subtable.UseLongDeltas ? 0x8000 : 0));
            data.UInt16(subtable.Regions.Length);
            foreach (var region in subtable.Regions)
            {
                data.UInt16(region);
            }

            foreach (var item in subtable.Items)
            {
                if (item.Length != subtable.Regions.Length)
                    throw new InvalidOperationException("An item of a variation store has one delta per region of its subtable");

                for (var column = 0; column < item.Length; column++)
                {
                    if (column < wordCount)
                    {
                        if (subtable.UseLongDeltas)
                        {
                            data.Int32(item[column]);
                        }
                        else
                        {
                            data.Int16(item[column]);
                        }
                    }
                    else if (subtable.UseLongDeltas)
                    {
                        data.Int16(item[column]);
                    }
                    else
                    {
                        data.Byte((byte)(sbyte)item[column]);
                    }
                }
            }

            subtables.Add(data.ToArray());
        }

        var writer = new FontWriter();
        writer.UInt16(1);
        var regionsOffset = 8 + (subtables.Count * 4);
        writer.UInt32((uint)regionsOffset);
        writer.UInt16(subtables.Count);
        var offset = regionsOffset + 4 + (store.Regions.Count * axisCount * 6);
        foreach (var subtable in subtables)
        {
            writer.UInt32((uint)offset);
            offset += subtable.Length;
        }

        writer.UInt16(axisCount);
        writer.UInt16(store.Regions.Count);
        foreach (var region in store.Regions)
        {
            if (region.Axes.Length != axisCount)
                throw new InvalidOperationException("A region has a range per axis");

            foreach (var (start, peak, end) in region.Axes)
            {
                writer.F2Dot14(start);
                writer.F2Dot14(peak);
                writer.F2Dot14(end);
            }
        }

        foreach (var subtable in subtables)
        {
            writer.Bytes(subtable);
        }

        return writer.ToArray();
    }

    /// <summary>Writes a delta set index map: the subtable and the item of each index.</summary>
    /// <param name="entries">The entries.</param>
    /// <param name="format">0 for a 16-bit count, 1 for a 32-bit count.</param>
    /// <param name="entrySize">The number of bytes of an entry, or 0 for the smallest that holds every entry.</param>
    public static byte[] WriteIndexMap(IReadOnlyList<(int Outer, int Inner)> entries, int format = 0, int entrySize = 0)
    {
        var innerBits = 1;
        while (entries.Any(entry => entry.Inner >= (1 << innerBits)))
        {
            innerBits++;
        }

        var largest = entries.Max(entry => ((long)entry.Outer << innerBits) | (uint)entry.Inner);
        var size = Math.Max(entrySize, largest <= 0xFF ? 1 : largest <= 0xFFFF ? 2 : largest <= 0xFFFFFF ? 3 : 4);
        var writer = new FontWriter();
        writer.Byte((byte)format);
        writer.Byte((byte)(((size - 1) << 4) | (innerBits - 1)));
        if (format == 0)
        {
            writer.UInt16(entries.Count);
        }
        else
        {
            writer.UInt32((uint)entries.Count);
        }

        foreach (var (outer, inner) in entries)
        {
            var value = ((long)outer << innerBits) | (uint)inner;
            for (var shift = (size - 1) * 8; shift >= 0; shift -= 8)
            {
                writer.Byte((byte)(value >> shift));
            }
        }

        return writer.ToArray();
    }

    /// <summary>Writes the <c>HVAR</c> table: a store, and optionally the map from the glyphs to its items.</summary>
    public static byte[] WriteHvar(FontVariationStore store, int axisCount, byte[]? advanceMap)
    {
        var storeData = WriteStore(store, axisCount);
        var writer = new FontWriter();
        writer.UInt16(1);
        writer.UInt16(0);
        writer.UInt32(20);
        writer.UInt32(advanceMap is null ? 0 : (uint)(20 + storeData.Length));
        writer.UInt32(0);
        writer.UInt32(0);
        writer.Bytes(storeData);
        if (advanceMap is not null)
        {
            writer.Bytes(advanceMap);
        }

        return writer.ToArray();
    }

    /// <summary>Writes the <c>MVAR</c> table: a store, and the item of each metric, by its tag.</summary>
    public static byte[] WriteMvar(FontVariationStore store, int axisCount, SortedDictionary<string, (int Outer, int Inner)> metrics)
    {
        var writer = new FontWriter();
        writer.UInt16(1);
        writer.UInt16(0);
        writer.UInt16(0);
        writer.UInt16(8);
        writer.UInt16(metrics.Count);
        writer.UInt16(12 + (metrics.Count * 8));
        foreach (var (tag, (outer, inner)) in metrics)
        {
            writer.Tag(tag);
            writer.UInt16(outer);
            writer.UInt16(inner);
        }

        writer.Bytes(WriteStore(store, axisCount));
        return writer.ToArray();
    }

    /// <summary>Writes the <c>gvar</c> table: the tuple variations of each glyph.</summary>
    /// <param name="glyphs">The glyphs of the font.</param>
    /// <param name="axisCount">The number of axes.</param>
    /// <param name="shareTuples">Whether the peaks of the regions are stored once for the table instead of in each tuple variation.</param>
    /// <param name="useLongOffsets">Whether the offsets of the glyphs are 32 bits.</param>
    public static byte[] WriteGvar(IReadOnlyList<FontGlyph> glyphs, int axisCount, bool shareTuples, bool useLongOffsets)
    {
        var sharedTuples = new List<double[]>();
        if (shareTuples)
        {
            foreach (var peak in glyphs.SelectMany(glyph => glyph.Variations).Select(variation => variation.Peak))
            {
                if (!sharedTuples.Any(shared => shared.AsSpan().SequenceEqual(peak)))
                {
                    sharedTuples.Add(peak);
                }
            }
        }

        var data = new List<byte[]>();
        foreach (var glyph in glyphs)
        {
            var glyphData = WriteGlyphVariations(glyph, axisCount, sharedTuples);

            // The short offsets are half offsets: the data of a glyph has an even length.
            data.Add(glyphData.Length % 2 == 0 ? glyphData : [.. glyphData, 0]);
        }

        var writer = new FontWriter();
        writer.UInt16(1);
        writer.UInt16(0);
        writer.UInt16(axisCount);
        writer.UInt16(sharedTuples.Count);
        var offsetsLength = (glyphs.Count + 1) * (useLongOffsets ? 4 : 2);
        var sharedTuplesOffset = 20 + offsetsLength;
        writer.UInt32((uint)sharedTuplesOffset);
        writer.UInt16(glyphs.Count);
        writer.UInt16(useLongOffsets ? 1 : 0);
        writer.UInt32((uint)(sharedTuplesOffset + (sharedTuples.Count * axisCount * 2)));
        var offset = 0;
        for (var i = 0; i <= data.Count; i++)
        {
            if (useLongOffsets)
            {
                writer.UInt32((uint)offset);
            }
            else
            {
                writer.UInt16(offset / 2);
            }

            if (i < data.Count)
            {
                offset += data[i].Length;
            }
        }

        foreach (var tuple in sharedTuples)
        {
            foreach (var value in tuple)
            {
                writer.F2Dot14(value);
            }
        }

        foreach (var glyphData in data)
        {
            writer.Bytes(glyphData);
        }

        return writer.ToArray();
    }

    private static byte[] WriteGlyphVariations(FontGlyph glyph, int axisCount, List<double[]> sharedTuples)
    {
        if (glyph.Variations.Count == 0)
            return [];

        var pointCount = (glyph.Components.Count > 0 ? glyph.Components.Count : glyph.Contours.Sum(contour => contour.Count)) + 4;
        var headers = new FontWriter();
        var serialized = new FontWriter();
        var sharedPoints = glyph.Variations.FirstOrDefault(variation => variation.UsesSharedPoints);
        if (sharedPoints is not null)
        {
            WritePoints(serialized, sharedPoints.Points, sharedPoints.WritePointsAsWords);
        }

        foreach (var variation in glyph.Variations)
        {
            if (variation.Peak.Length != axisCount)
                throw new InvalidOperationException("A tuple variation has a peak value per axis");

            if (variation.Deltas.Length != (variation.Points?.Length ?? pointCount))
                throw new InvalidOperationException($"A tuple variation has {variation.Deltas.Length} deltas for {variation.Points?.Length ?? pointCount} points");

            var tuple = new FontWriter();
            var flags = 0;
            if (!variation.UsesSharedPoints)
            {
                flags |= 0x2000;
                WritePoints(tuple, variation.Points, variation.WritePointsAsWords);
            }

            WriteDeltas(tuple, [.. variation.Deltas.Select(delta => delta.X)]);
            WriteDeltas(tuple, [.. variation.Deltas.Select(delta => delta.Y)]);

            var shared = sharedTuples.FindIndex(candidate => candidate.AsSpan().SequenceEqual(variation.Peak));
            flags |= shared >= 0 ? shared : 0x8000;
            flags |= variation.Start is not null ? 0x4000 : 0;
            headers.UInt16(tuple.Length);
            headers.UInt16(flags);
            if (shared < 0)
            {
                foreach (var value in variation.Peak)
                {
                    headers.F2Dot14(value);
                }
            }

            if (variation.Start is not null)
            {
                foreach (var value in variation.Start.Concat(variation.End!))
                {
                    headers.F2Dot14(value);
                }
            }

            serialized.Bytes(tuple.ToArray());
        }

        var writer = new FontWriter();
        writer.UInt16(glyph.Variations.Count | (sharedPoints is not null ? 0x8000 : 0));
        writer.UInt16(4 + headers.Length);
        writer.Bytes(headers.ToArray());
        writer.Bytes(serialized.ToArray());
        return writer.ToArray();
    }

    /// <summary>Writes packed point numbers: the count, then runs of differences of one or two bytes.</summary>
    private static void WritePoints(FontWriter writer, int[]? points, bool asWords)
    {
        if (points is null)
        {
            writer.Byte(0);
            return;
        }

        if (points.Length == 0)
            throw new InvalidOperationException("A count of zero point numbers stands for every point: a tuple variation names at least one point");

        if (points.Length < 0x80)
        {
            writer.Byte((byte)points.Length);
        }
        else
        {
            writer.UInt16(points.Length | 0x8000);
        }

        var differences = new int[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            differences[i] = points[i] - (i == 0 ? 0 : points[i - 1]);
        }

        for (var i = 0; i < differences.Length;)
        {
            var isWord = asWords || differences[i] > 0xFF;
            var run = 1;
            while (run < 128 && i + run < differences.Length && (asWords || differences[i + run] > 0xFF) == isWord)
            {
                run++;
            }

            writer.Byte((byte)((run - 1) | (isWord ? 0x80 : 0)));
            for (var j = 0; j < run; j++)
            {
                if (isWord)
                {
                    writer.UInt16(differences[i + j]);
                }
                else
                {
                    writer.Byte((byte)differences[i + j]);
                }
            }

            i += run;
        }
    }

    /// <summary>Writes packed deltas: runs of zeros, of bytes, of words or of 32-bit values.</summary>
    private static void WriteDeltas(FontWriter writer, int[] deltas)
    {
        for (var i = 0; i < deltas.Length;)
        {
            var kind = GetKind(deltas[i]);
            var run = 1;
            while (run < 64 && i + run < deltas.Length && GetKind(deltas[i + run]) == kind)
            {
                run++;
            }

            writer.Byte((byte)((run - 1) | kind));
            for (var j = 0; j < run; j++)
            {
                switch (kind)
                {
                    case 0x00:
                        writer.Byte((byte)(sbyte)deltas[i + j]);
                        break;

                    case 0x40:
                        writer.Int16(deltas[i + j]);
                        break;

                    case 0xC0:
                        writer.Int32(deltas[i + j]);
                        break;
                }
            }

            i += run;
        }

        static int GetKind(int delta) => delta switch
        {
            0 => 0x80,
            >= sbyte.MinValue and <= sbyte.MaxValue => 0x00,
            >= short.MinValue and <= short.MaxValue => 0x40,
            _ => 0xC0,
        };
    }

    private static int ToFixed(double value) => (int)Math.Round(value * 65536, MidpointRounding.AwayFromZero);
}
