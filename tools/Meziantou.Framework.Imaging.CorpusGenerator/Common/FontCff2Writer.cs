namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>
/// Writes a CFF2 table from charstrings: OpenType specification, "CFF2 - Compact Font Format (CFF) Version 2".
/// </summary>
/// <remarks>
/// The table is a header, a Top DICT, the global subroutines, then - at the offsets the Top DICT gives - the variation
/// store, the font DICT selector, the charstrings, the font DICTs, and the private DICT and the local subroutines of
/// each font DICT. The charstrings are written as given: <see cref="FontCharstring"/> encodes them.
/// </remarks>
internal sealed class FontCff2Writer
{
    public List<byte[]> CharStrings { get; } = [];

    public List<byte[]> GlobalSubroutines { get; } = [];

    /// <summary>Gets the local subroutines of each font DICT. The table has one font DICT per list.</summary>
    public List<List<byte[]>> LocalSubroutines { get; } = [[]];

    /// <summary>Gets or sets the font DICT of each glyph; <see langword="null"/> for a table without selector, whose glyphs all use the first one.</summary>
    public int[]? FontDictOfGlyphs { get; set; }

    /// <summary>Gets or sets the format of the selector: 0, 3 or 4.</summary>
    public int FontDictSelectFormat { get; set; } = 3;

    /// <summary>Gets or sets the variation store the charstrings blend with.</summary>
    public FontVariationStore? VariationStore { get; set; }

    /// <summary>Gets the subtable of the store each font DICT blends with by default; a font DICT without entry uses the first one.</summary>
    public Dictionary<int, int> VariationStoreIndexes { get; } = [];

    /// <summary>Makes a font builder an OpenType font with the outlines of this table: no TrueType table, a short <c>maxp</c>.</summary>
    public void Apply(FontBuilder builder)
    {
        if (builder.Glyphs.Count != CharStrings.Count)
            throw new InvalidOperationException("The font builder needs one glyph, without outline, per charstring");

        builder.SfntVersion = 0x4F54544F;
        builder.RawTables["CFF2"] = Write(builder.Axes.Count);
        builder.OmittedTables.Add("glyf");
        builder.OmittedTables.Add("loca");

        // Version 0.5 of the table: the number of glyphs only.
        var maxp = new FontWriter();
        maxp.UInt32(0x00005000);
        maxp.UInt16(CharStrings.Count);
        builder.RawTables["maxp"] = maxp.ToArray();
    }

    public byte[] Write(int axisCount)
    {
        const int HeaderSize = 5;
        var globalSubroutines = WriteIndex(GlobalSubroutines);
        var charStrings = WriteIndex(CharStrings);
        var store = Array.Empty<byte>();
        if (VariationStore is not null)
        {
            // The store follows its length.
            var data = FontVariationWriter.WriteStore(VariationStore, axisCount);
            var storeWriter = new FontWriter();
            storeWriter.UInt16(data.Length);
            storeWriter.Bytes(data);
            store = storeWriter.ToArray();
        }

        var fdSelect = FontDictOfGlyphs is null ? [] : WriteFontDictSelect();

        // Every offset of the Top DICT and of the font DICTs is written in five bytes, so that the sizes are known
        // before the offsets.
        var topDictLength = 6 + 7 + (store.Length > 0 ? 6 : 0) + (fdSelect.Length > 0 ? 7 : 0);
        var storeOffset = HeaderSize + topDictLength + globalSubroutines.Length;
        var fdSelectOffset = storeOffset + store.Length;
        var charStringsOffset = fdSelectOffset + fdSelect.Length;
        var fdArrayOffset = charStringsOffset + charStrings.Length;

        // A private DICT: the default subtable of the store, then the offset of the local subroutines, which follow it.
        var privateDicts = new List<byte[]>();
        for (var i = 0; i < LocalSubroutines.Count; i++)
        {
            var dict = new FontWriter();
            if (VariationStoreIndexes.TryGetValue(i, out var storeIndex))
            {
                WriteDictInteger(dict, storeIndex);
                dict.Byte(22);
            }

            if (LocalSubroutines[i].Count > 0)
            {
                // Five bytes for the offset and one for the operator: the subroutines start after them.
                WriteDictOffset(dict, dict.Length + 6);
                dict.Byte(19);
                dict.Bytes(WriteIndex(LocalSubroutines[i]));
            }

            privateDicts.Add(dict.ToArray());
        }

        // The font DICTs are 11 bytes each: the size and the offset of the private DICT, then the operator.
        var fontDicts = new List<byte[]>();
        var fdArrayLength = WriteIndex([.. LocalSubroutines.Select(_ => new byte[11])]).Length;
        var privateOffset = fdArrayOffset + fdArrayLength;
        for (var i = 0; i < privateDicts.Count; i++)
        {
            var subroutines = LocalSubroutines[i].Count > 0 ? WriteIndex(LocalSubroutines[i]).Length : 0;
            var dict = new FontWriter();
            WriteDictOffset(dict, privateDicts[i].Length - subroutines);
            WriteDictOffset(dict, privateOffset);
            dict.Byte(18);
            fontDicts.Add(dict.ToArray());
            privateOffset += privateDicts[i].Length;
        }

        var writer = new FontWriter();
        writer.Byte(2);
        writer.Byte(0);
        writer.Byte(HeaderSize);
        writer.UInt16(topDictLength);
        WriteDictOffset(writer, charStringsOffset);
        writer.Byte(17);
        WriteDictOffset(writer, fdArrayOffset);
        writer.Byte(12);
        writer.Byte(36);
        if (store.Length > 0)
        {
            WriteDictOffset(writer, storeOffset);
            writer.Byte(24);
        }

        if (fdSelect.Length > 0)
        {
            WriteDictOffset(writer, fdSelectOffset);
            writer.Byte(12);
            writer.Byte(37);
        }

        writer.Bytes(globalSubroutines);
        writer.Bytes(store);
        writer.Bytes(fdSelect);
        writer.Bytes(charStrings);
        writer.Bytes(WriteIndex(fontDicts));
        foreach (var dict in privateDicts)
        {
            writer.Bytes(dict);
        }

        return writer.ToArray();
    }

    /// <summary>Writes an INDEX of a CFF2 table: the number of objects on 32 bits, the size of the offsets, the offsets from 1, the objects.</summary>
    public static byte[] WriteIndex(IReadOnlyList<byte[]> objects)
    {
        var writer = new FontWriter();
        writer.UInt32((uint)objects.Count);
        if (objects.Count == 0)
            return writer.ToArray();

        var total = objects.Sum(item => item.Length) + 1;
        var offsetSize = total <= 0xFF ? 1 : total <= 0xFFFF ? 2 : total <= 0xFFFFFF ? 3 : 4;
        writer.Byte((byte)offsetSize);
        var offset = 1;
        for (var i = 0; i <= objects.Count; i++)
        {
            for (var shift = (offsetSize - 1) * 8; shift >= 0; shift -= 8)
            {
                writer.Byte((byte)(offset >> shift));
            }

            if (i < objects.Count)
            {
                offset += objects[i].Length;
            }
        }

        foreach (var item in objects)
        {
            writer.Bytes(item);
        }

        return writer.ToArray();
    }

    private byte[] WriteFontDictSelect()
    {
        var fontDicts = FontDictOfGlyphs!;
        var writer = new FontWriter();
        writer.Byte((byte)FontDictSelectFormat);
        if (FontDictSelectFormat == 0)
        {
            foreach (var fontDict in fontDicts)
            {
                writer.Byte((byte)fontDict);
            }

            return writer.ToArray();
        }

        // Formats 3 and 4: the first glyph and the font DICT of each range, then the number of glyphs; on 16 and 8
        // bits, or on 32 and 16 bits.
        var starts = Enumerable.Range(0, fontDicts.Length).Where(glyph => glyph == 0 || fontDicts[glyph] != fontDicts[glyph - 1]).ToArray();
        if (FontDictSelectFormat == 3)
        {
            writer.UInt16(starts.Length);
            foreach (var start in starts)
            {
                writer.UInt16(start);
                writer.Byte((byte)fontDicts[start]);
            }

            writer.UInt16(fontDicts.Length);
        }
        else
        {
            writer.UInt32((uint)starts.Length);
            foreach (var start in starts)
            {
                writer.UInt32((uint)start);
                writer.UInt16(fontDicts[start]);
            }

            writer.UInt32((uint)fontDicts.Length);
        }

        return writer.ToArray();
    }

    private static void WriteDictInteger(FontWriter writer, int value)
    {
        switch (value)
        {
            case >= -107 and <= 107:
                writer.Byte((byte)(value + 139));
                break;

            case >= 108 and <= 1131:
                writer.Byte((byte)(((value - 108) >> 8) + 247));
                writer.Byte((byte)(value - 108));
                break;

            default:
                writer.Byte(28);
                writer.Int16(value);
                break;
        }
    }

    private static void WriteDictOffset(FontWriter writer, int value)
    {
        writer.Byte(29);
        writer.Int32(value);
    }
}
