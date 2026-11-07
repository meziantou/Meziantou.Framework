namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>
/// Writes a <c>CFF </c> table (Adobe Technical Note #5176) from charstrings given as bytes, and makes a
/// <see cref="FontBuilder"/> an OpenType font with CFF outlines.
/// </summary>
internal sealed class FontCffWriter
{
    /// <summary>Gets the charstrings of the glyphs.</summary>
    public List<byte[]> CharStrings { get; } = [];

    public List<byte[]> GlobalSubroutines { get; } = [];

    /// <summary>
    /// Gets the local subroutines of each font DICT. A font with one entry and no <see cref="FontDictOfGlyphs"/> has
    /// named glyphs; the other ones are CID-keyed.
    /// </summary>
    public List<List<byte[]>> LocalSubroutines { get; } = [[]];

    /// <summary>Gets or sets the font DICT of each glyph, which makes the font CID-keyed.</summary>
    public int[]? FontDictOfGlyphs { get; set; }

    /// <summary>Gets or sets the format of the font DICT selector: 0 (one byte per glyph) or 3 (ranges).</summary>
    public int FontDictSelectFormat { get; set; } = 3;

    /// <summary>Gets or sets the size of the offsets of the INDEX structures, from 1 to 4 bytes.</summary>
    public int OffsetSize { get; set; } = 1;

    /// <summary>Gets or sets the type of the charstrings the Top DICT declares, when it is not the default one (2).</summary>
    public int? CharstringType { get; set; }

    /// <summary>Makes a font builder an OpenType font with the outlines of this table: no TrueType table, a short <c>maxp</c>.</summary>
    public void Apply(FontBuilder builder)
    {
        if (builder.Glyphs.Count != CharStrings.Count)
            throw new InvalidOperationException("The font builder needs one glyph, without outline, per charstring");

        builder.SfntVersion = 0x4F54544F;
        builder.RawTables["CFF "] = Write(builder.PostScriptName ?? "Font");
        builder.OmittedTables.Add("glyf");
        builder.OmittedTables.Add("loca");

        // Version 0.5 of the table: the number of glyphs only.
        var maxp = new FontWriter();
        maxp.UInt32(0x00005000);
        maxp.UInt16(CharStrings.Count);
        builder.RawTables["maxp"] = maxp.ToArray();
    }

    public byte[] Write(string name)
    {
        var isCidKeyed = FontDictOfGlyphs is not null;
        var names = WriteIndex([Encoding.ASCII.GetBytes(name)]);
        var strings = WriteIndex(isCidKeyed ? ["Adobe"u8.ToArray(), "Identity"u8.ToArray()] : []);
        var globalSubroutines = WriteIndex(GlobalSubroutines);
        var charStrings = WriteIndex(CharStrings);

        // The charset: one range that gives the glyphs after .notdef the identifiers 1, 2, 3...
        var charset = new FontWriter();
        charset.Byte(2);
        if (CharStrings.Count > 1)
        {
            charset.UInt16(1);
            charset.UInt16(CharStrings.Count - 2);
        }

        // Each private DICT is followed by its local subroutines, which it places relative to its own start.
        var privates = new List<(byte[] Dict, byte[] Subroutines)>();
        foreach (var subroutines in LocalSubroutines)
        {
            var dict = new FontWriter();
            WriteDictInteger(dict, 0);
            dict.Byte(20);
            if (subroutines.Count > 0)
            {
                WriteDictOffset(dict, dict.Length + 6);
                dict.Byte(19);
            }

            privates.Add((dict.ToArray(), subroutines.Count > 0 ? WriteIndex(subroutines) : []));
        }

        // The offsets of the Top DICT are written in five bytes: its length does not depend on their values.
        byte[] WriteTopDict(int charsetOffset, int charStringsOffset, int privateOffset, int fdArrayOffset, int fdSelectOffset)
        {
            var dict = new FontWriter();
            if (isCidKeyed)
            {
                // Registry, ordering (the two custom strings, after the 391 standard ones) and supplement.
                WriteDictInteger(dict, 391);
                WriteDictInteger(dict, 392);
                WriteDictInteger(dict, 0);
                dict.Byte(12);
                dict.Byte(30);
                WriteDictInteger(dict, CharStrings.Count);
                dict.Byte(12);
                dict.Byte(34);
                WriteDictOffset(dict, fdArrayOffset);
                dict.Byte(12);
                dict.Byte(36);
                WriteDictOffset(dict, fdSelectOffset);
                dict.Byte(12);
                dict.Byte(37);
            }
            else
            {
                WriteDictInteger(dict, privates[0].Dict.Length);
                WriteDictOffset(dict, privateOffset);
                dict.Byte(18);
            }

            if (CharstringType is { } type)
            {
                WriteDictInteger(dict, type);
                dict.Byte(12);
                dict.Byte(6);
            }

            WriteDictOffset(dict, charsetOffset);
            dict.Byte(15);
            WriteDictOffset(dict, charStringsOffset);
            dict.Byte(17);
            return dict.ToArray();
        }

        var topDictLength = WriteIndex([WriteTopDict(0, 0, 0, 0, 0)]).Length;
        var position = 4 + names.Length + topDictLength + strings.Length + globalSubroutines.Length;
        var charsetOffset = position;
        position += charset.Length;
        var charStringsOffset = position;
        position += charStrings.Length;
        var privateOffsets = new int[privates.Count];
        for (var i = 0; i < privates.Count; i++)
        {
            privateOffsets[i] = position;
            position += privates[i].Dict.Length + privates[i].Subroutines.Length;
        }

        byte[] fdArray = [];
        byte[] fdSelect = [];
        var fdArrayOffset = 0;
        var fdSelectOffset = 0;
        if (isCidKeyed)
        {
            var fontDicts = new List<byte[]>();
            for (var i = 0; i < privates.Count; i++)
            {
                var dict = new FontWriter();
                WriteDictInteger(dict, privates[i].Dict.Length);
                WriteDictOffset(dict, privateOffsets[i]);
                dict.Byte(18);
                fontDicts.Add(dict.ToArray());
            }

            fdArray = WriteIndex(fontDicts);
            fdArrayOffset = position;
            position += fdArray.Length;
            fdSelect = WriteFontDictSelect();
            fdSelectOffset = position;
        }

        var writer = new FontWriter();
        writer.Byte(1);
        writer.Byte(0);
        writer.Byte(4);
        writer.Byte(4);
        writer.Bytes(names);
        writer.Bytes(WriteIndex([WriteTopDict(charsetOffset, charStringsOffset, privateOffsets[0], fdArrayOffset, fdSelectOffset)]));
        writer.Bytes(strings);
        writer.Bytes(globalSubroutines);
        writer.Bytes(charset.ToArray());
        writer.Bytes(charStrings);
        foreach (var (dict, subroutines) in privates)
        {
            writer.Bytes(dict);
            writer.Bytes(subroutines);
        }

        writer.Bytes(fdArray);
        writer.Bytes(fdSelect);
        return writer.ToArray();
    }

    /// <summary>Writes an INDEX: the number of objects, the size of the offsets, the offsets from 1, the objects.</summary>
    public byte[] WriteIndex(IReadOnlyList<byte[]> objects)
    {
        var writer = new FontWriter();
        writer.UInt16(objects.Count);
        if (objects.Count == 0)
            return writer.ToArray();

        var total = objects.Sum(item => item.Length) + 1;
        var offsetSize = Math.Max(OffsetSize, total <= 0xFF ? 1 : total <= 0xFFFF ? 2 : total <= 0xFFFFFF ? 3 : 4);
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

        // Format 3: the first glyph and the font DICT of each range, then the number of glyphs.
        var starts = Enumerable.Range(0, fontDicts.Length).Where(glyph => glyph == 0 || fontDicts[glyph] != fontDicts[glyph - 1]).ToArray();
        writer.UInt16(starts.Length);
        foreach (var start in starts)
        {
            writer.UInt16(start);
            writer.Byte((byte)fontDicts[start]);
        }

        writer.UInt16(fontDicts.Length);
        return writer.ToArray();
    }

    /// <summary>Writes an integer of a DICT in its shortest encoding.</summary>
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

    /// <summary>Writes an integer of a DICT in five bytes, whatever its value.</summary>
    private static void WriteDictOffset(FontWriter writer, int value)
    {
        writer.Byte(29);
        writer.Int32(value);
    }
}
