namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The properties of a glyph for the OpenType layout (HarfBuzz's <c>hb_ot_layout_glyph_props_flags_t</c>): its class (the
/// same bits as the lookup flags that skip it), whether it was substituted, ligated or multiplied by the GSUB lookups, and
/// its mark attachment class in the high byte.
/// </summary>
[Flags]
internal enum GlyphProperties : ushort
{
    None = 0,
    BaseGlyph = 0x02,
    Ligature = 0x04,
    Mark = 0x08,
    ClassMask = BaseGlyph | Ligature | Mark,

    /// <summary>The glyph was substituted (a default ignorable character is then visible).</summary>
    Substituted = 0x10,

    /// <summary>The glyph is the result of a ligature substitution.</summary>
    Ligated = 0x20,

    /// <summary>The glyph is one of the results of a multiple substitution.</summary>
    Multiplied = 0x40,

    /// <summary>The flags kept when the class of a glyph changes.</summary>
    Preserve = Substituted | Ligated | Multiplied,
}

/// <summary>
/// The GDEF table of a font (https://learn.microsoft.com/typography/opentype/spec/gdef): the glyph classes, the mark
/// attachment classes, the mark glyph sets, the ligature caret values and the item variation store of the variable fonts.
/// </summary>
internal sealed class OpenTypeGdef
{
    private readonly byte[] _data;
    private readonly int _start;
    private readonly int _length;
    private readonly int _glyphClassDefinition;
    private readonly int _ligatureCaretList;
    private readonly int _markAttachClassDefinition;
    private readonly int _markGlyphSets;
    private readonly int _variationStore;

    private OpenTypeGdef(byte[] data, int start, int length)
    {
        _data = data;
        _start = start;
        _length = length;
        var table = Data;
        var minor = FontBinary.U16(table, 2);
        _glyphClassDefinition = FontBinary.U16(table, 4);
        _ligatureCaretList = FontBinary.U16(table, 8);
        _markAttachClassDefinition = FontBinary.U16(table, 10);
        _markGlyphSets = minor >= 2 ? FontBinary.U16(table, 12) : 0;
        _variationStore = minor >= 3 ? (int)Math.Min(FontBinary.U32(table, 14), int.MaxValue) : 0;
        MarkGlyphSetCount = _markGlyphSets == 0 ? 0 : FontBinary.U16(table, _markGlyphSets + 2);
    }

    private ReadOnlySpan<byte> Data => _data.AsSpan(_start, _length);

    /// <summary>Whether the table classifies the glyphs (it has a glyph class definition table).</summary>
    public bool HasGlyphClasses => _glyphClassDefinition != 0;

    /// <summary>The number of mark glyph sets.</summary>
    public int MarkGlyphSetCount { get; }

    /// <summary>The item variation store (empty when there is none).</summary>
    public ReadOnlySpan<byte> VariationStore => _variationStore == 0 ? default : FontBinary.Slice(Data, _variationStore);

    internal static OpenTypeGdef? Create(byte[] data, int offset, int length) => offset < 0 || length < 12 || FontBinary.U16(data.AsSpan(offset, length), 0) != 1 ? null : new OpenTypeGdef(data, offset, length);

    /// <summary>The class of a glyph (https://learn.microsoft.com/typography/opentype/spec/gdef#glyph-class-definition-table): 1 base, 2 ligature, 3 mark, 4 component; 0 when it is not classified.</summary>
    public int GetGlyphClass(int glyph) => OpenTypeLayoutTable.GetClass(Data, _glyphClassDefinition, glyph);

    private const int PropertiesCacheSize = 512;
    private readonly uint[] _propertiesCache = new uint[PropertiesCacheSize];

    /// <summary>The properties of a glyph from its class (the mark attachment class of a mark in the high byte), as HarfBuzz's <c>GDEF::get_glyph_props</c>.</summary>
    public GlyphProperties GetGlyphProperties(int glyph)
    {
        // A direct-mapped cache (each entry is written atomically: the glyph plus 1, then the properties)
        ref var entry = ref _propertiesCache[glyph & (PropertiesCacheSize - 1)];
        var cached = Volatile.Read(ref entry);
        if ((int)(cached >> 16) == glyph + 1)
            return (GlyphProperties)(ushort)cached;

        var properties = GetGlyphClass(glyph) switch
        {
            1 => GlyphProperties.BaseGlyph,
            2 => GlyphProperties.Ligature,
            3 => GlyphProperties.Mark | (GlyphProperties)(OpenTypeLayoutTable.GetClass(Data, _markAttachClassDefinition, glyph) << 8),
            _ => GlyphProperties.None,
        };

        Volatile.Write(ref entry, ((uint)(glyph + 1) << 16) | (ushort)properties);
        return properties;
    }

    /// <summary>Whether a mark glyph set contains a glyph (https://learn.microsoft.com/typography/opentype/spec/gdef#mark-glyph-sets-table).</summary>
    public bool MarkSetCovers(int set, int glyph)
    {
        if ((uint)set >= (uint)MarkGlyphSetCount)
            return false;

        var table = Data;
        var coverage = FontBinary.U32(table, _markGlyphSets + 4 + (set * 4));
        return coverage != 0 && coverage <= int.MaxValue - _markGlyphSets && OpenTypeLayoutTable.GetCoverageIndex(table, _markGlyphSets + (int)coverage, glyph) >= 0;
    }

    /// <summary>
    /// The caret positions of a ligature glyph in font units from its origin, in increasing order (https://learn.microsoft.com/typography/opentype/spec/gdef#ligature-caret-list-table):
    /// the coordinates of the formats 1 and 3 (with the variation deltas); the contour points of format 2 are not supported
    /// (returns false). Returns false when the glyph has no caret values.
    /// </summary>
    public bool TryGetLigatureCarets(int glyph, FontVariationInstance? instance, Span<double> carets, out int count)
    {
        count = 0;
        if (_ligatureCaretList == 0)
            return false;

        var table = Data;
        var index = OpenTypeLayoutTable.GetCoverageIndex(table, OpenTypeLayoutTable.Offset16(table, _ligatureCaretList, _ligatureCaretList), glyph);
        if (index < 0 || index >= FontBinary.U16(table, _ligatureCaretList + 2))
            return false;

        var ligatureGlyph = OpenTypeLayoutTable.Offset16(table, _ligatureCaretList, _ligatureCaretList + 4 + (index * 2));
        var caretCount = FontBinary.U16(table, ligatureGlyph);
        if (ligatureGlyph == 0 || caretCount == 0 || caretCount > carets.Length)
            return false;

        for (var i = 0; i < caretCount; i++)
        {
            var caret = OpenTypeLayoutTable.Offset16(table, ligatureGlyph, ligatureGlyph + 2 + (i * 2));
            switch (FontBinary.U16(table, caret))
            {
                case 1:
                    carets[i] = FontBinary.I16(table, caret + 2);
                    break;
                case 3:
                    carets[i] = FontBinary.I16(table, caret + 2) + GetDeviceDelta(caret, FontBinary.U16(table, caret + 4), instance);
                    break;
                default:
                    return false;
            }
        }

        count = caretCount;
        return true;
    }

    /// <summary>
    /// The variation delta of a device table at <paramref name="deviceOffset"/> from <paramref name="baseOffset"/> in the
    /// table that contains it: a VariationIndex table (https://learn.microsoft.com/typography/opentype/spec/chapter2#variationindex-table)
    /// gives a delta of the item variation store for the instance; the hinting device tables are ignored (no ppem).
    /// </summary>
    public double GetDeviceDelta(ReadOnlySpan<byte> table, int baseOffset, int deviceOffset, FontVariationInstance? instance)
    {
        if (deviceOffset == 0 || instance is null || instance.IsDefault)
            return 0;

        var device = baseOffset + deviceOffset;
        return FontBinary.U16(table, device + 4) == 0x8000 ? instance.Face.GetLayoutDelta(FontBinary.U16(table, device), FontBinary.U16(table, device + 2)) : 0;
    }

    private double GetDeviceDelta(int baseOffset, int deviceOffset, FontVariationInstance? instance) => GetDeviceDelta(Data, baseOffset, deviceOffset, instance);
}
