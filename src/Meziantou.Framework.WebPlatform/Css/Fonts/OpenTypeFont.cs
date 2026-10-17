using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// The metrics of a font, in font units (divide by <see cref="OpenTypeFont.UnitsPerEm"/> and multiply by the font size to
/// get CSS pixels). The vertical distances are positive upward from the baseline, except <see cref="Descent"/> and the
/// subscript offset, which are positive downward.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FontMetrics
{
    /// <summary>The ascent of the font (https://drafts.csswg.org/css-inline-3/#ascent-descent).</summary>
    public double Ascent { get; init; }

    /// <summary>The descent of the font, positive below the baseline.</summary>
    public double Descent { get; init; }

    /// <summary>The line gap of the font.</summary>
    public double LineGap { get; init; }

    /// <summary>The x-height of the font, or null when the font does not define it and has no glyph <c>x</c> with an outline.</summary>
    public double? XHeight { get; init; }

    /// <summary>The cap height of the font, or null when the font does not define it and has no glyph <c>H</c> with an outline.</summary>
    public double? CapHeight { get; init; }

    /// <summary>The position of the top of the underline (negative below the baseline).</summary>
    public double UnderlinePosition { get; init; }

    /// <summary>The thickness of the underline.</summary>
    public double UnderlineThickness { get; init; }

    /// <summary>The position of the top of the strikeout stroke.</summary>
    public double StrikeoutPosition { get; init; }

    /// <summary>The thickness of the strikeout stroke.</summary>
    public double StrikeoutThickness { get; init; }

    /// <summary>The vertical offset of the subscripts, positive below the baseline.</summary>
    public double SubscriptOffset { get; init; }

    /// <summary>The vertical offset of the superscripts, positive above the baseline.</summary>
    public double SuperscriptOffset { get; init; }
}

/// <summary>The bounding box of the outline of a glyph, in font units.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct GlyphBounds(short XMin, short YMin, short XMax, short YMax);

/// <summary>
/// A font face of a <see cref="FontFile"/>: its tables are read by the managed reader (https://learn.microsoft.com/typography/opentype/spec/).
/// The structure of the tables used for the layout (<c>head</c>, <c>hhea</c>, <c>maxp</c>, <c>hmtx</c>, <c>cmap</c>,
/// <c>loca</c>) is validated when the face is created; the other tables are read with bounds checks: a malformed table
/// gives zeros, never an exception.
/// </summary>
internal sealed partial class OpenTypeFont
{
    private const int TableRecordLength = 16;
    private const int MaxTableCount = 512;

    private readonly uint[] _tableTags;
    private readonly int[] _tableOffsets;
    private readonly int[] _tableLengths;

    // The offsets of the tables used for each glyph (-1 when the table is missing)
    private readonly int _hmtxOffset;
    private readonly int _hmtxLength;
    private readonly int _vmtxOffset;
    private readonly int _vmtxLength;
    private readonly int _glyfOffset;
    private readonly int _glyfLength;
    private readonly int _locaOffset;
    private readonly int _locaLength;
    private readonly bool _isLongLoca;
    private readonly int _horizontalMetricsCount;
    private readonly int _verticalMetricsCount;

    // The cmap subtables (https://learn.microsoft.com/typography/opentype/spec/cmap): the offsets in the data, or -1
    private readonly int _cmapSubtable;
    private readonly ushort _cmapFormat;
    private readonly bool _isSymbolCmap;
    private readonly int _cmapVariationSubtable;

    private OpenTypeFont(FontFile file, int faceIndex, uint[] tags, int[] offsets, int[] lengths)
    {
        File = file;
        FaceIndex = faceIndex;
        _tableTags = tags;
        _tableOffsets = offsets;
        _tableLengths = lengths;

        var data = Data;
        var head = GetTable(OpenTypeTag.Head);
        UnitsPerEm = FontBinary.U16(head, 18);
        HeadFlags = FontBinary.U16(head, 16);
        MaxCharWidth = FontBinary.I16(head, 40) - FontBinary.I16(head, 36);
        MacStyle = FontBinary.U16(head, 44);
        _isLongLoca = FontBinary.I16(head, 50) == 1;
        GlyphCount = FontBinary.U16(GetTable(OpenTypeTag.Maxp), 4);

        var hhea = GetTable(OpenTypeTag.Hhea);
        HheaAscender = FontBinary.I16(hhea, 4);
        HheaDescender = FontBinary.I16(hhea, 6);
        HheaLineGap = FontBinary.I16(hhea, 8);
        _horizontalMetricsCount = Math.Min(FontBinary.U16(hhea, 34), GlyphCount);
        (_hmtxOffset, _hmtxLength) = GetTableRange(OpenTypeTag.Hmtx);

        // https://learn.microsoft.com/typography/opentype/spec/vhea
        var vhea = GetTable(OpenTypeTag.Vhea);
        (_vmtxOffset, _vmtxLength) = GetTableRange(OpenTypeTag.Vmtx);
        if (vhea.Length >= 36 && _vmtxOffset >= 0)
        {
            HasVerticalMetrics = true;
            VerticalAscender = FontBinary.I16(vhea, 4);
            VerticalDescender = FontBinary.I16(vhea, 6);
            VerticalLineGap = FontBinary.I16(vhea, 8);
            _verticalMetricsCount = Math.Min(FontBinary.U16(vhea, 34), Math.Min(GlyphCount, _vmtxLength / 4));
            HasVerticalMetrics = _verticalMetricsCount > 0;
        }

        (_glyfOffset, _glyfLength) = GetTableRange(OpenTypeTag.Glyf);
        (_locaOffset, _locaLength) = GetTableRange(OpenTypeTag.Loca);

        // https://learn.microsoft.com/typography/opentype/spec/os2
        var os2 = GetTable(OpenTypeTag.OS2);
        HasOS2 = os2.Length >= 68;
        if (HasOS2)
        {
            OS2Version = FontBinary.U16(os2, 0);
            AverageCharWidth = FontBinary.I16(os2, 2);
            WeightClass = FontBinary.U16(os2, 4);
            WidthClass = FontBinary.U16(os2, 6);
            SubscriptYOffset = FontBinary.I16(os2, 16);
            SuperscriptYOffset = FontBinary.I16(os2, 24);
            StrikeoutSize = FontBinary.I16(os2, 26);
            StrikeoutPosition = FontBinary.I16(os2, 28);
            FsSelection = FontBinary.U16(os2, 62);
            if (os2.Length >= 78)
            {
                TypoAscender = FontBinary.I16(os2, 68);
                TypoDescender = FontBinary.I16(os2, 70);
                TypoLineGap = FontBinary.I16(os2, 72);
                WinAscent = FontBinary.U16(os2, 74);
                WinDescent = FontBinary.U16(os2, 76);
            }

            if (OS2Version >= 2 && os2.Length >= 90)
            {
                OS2XHeight = FontBinary.I16(os2, 86);
                OS2CapHeight = FontBinary.I16(os2, 88);
            }
        }

        // https://learn.microsoft.com/typography/opentype/spec/post
        var post = GetTable(OpenTypeTag.Post);
        ItalicAngle = FontBinary.Fixed(post, 4);
        UnderlinePosition = FontBinary.I16(post, 8);
        UnderlineThickness = FontBinary.I16(post, 10);
        IsFixedPitch = FontBinary.U32(post, 12) != 0;

        FamilyName = GetName(NameId.TypographicFamily) ?? GetName(NameId.Family) ?? "";
        SubfamilyName = GetName(NameId.TypographicSubfamily) ?? GetName(NameId.Subfamily) ?? "";
        FullName = GetName(NameId.FullName) ?? "";
        PostScriptName = GetName(NameId.PostScriptName) ?? "";

        (_cmapSubtable, _cmapFormat, _isSymbolCmap, _cmapVariationSubtable) = SelectCmapSubtables(data, GetTableRange(OpenTypeTag.Cmap).Offset);
        Variations = new FontVariations(this);
    }

    /// <summary>The identifiers of the names of the name table (https://learn.microsoft.com/typography/opentype/spec/name#name-ids).</summary>
    internal static class NameId
    {
        public const int Copyright = 0;
        public const int Family = 1;
        public const int Subfamily = 2;
        public const int FullName = 4;
        public const int PostScriptName = 6;
        public const int License = 13;
        public const int TypographicFamily = 16;
        public const int TypographicSubfamily = 17;
    }

    /// <summary>The font file of the face.</summary>
    public FontFile File { get; }

    /// <summary>The index of the face in its file (a collection may have several faces).</summary>
    public int FaceIndex { get; }

    /// <summary>The sfnt data of the file (shared by the faces of a collection).</summary>
    public byte[] Data => File.Data;

    /// <summary>The number of font units per em (<c>head.unitsPerEm</c>).</summary>
    public int UnitsPerEm { get; }

    /// <summary>The number of glyphs (<c>maxp.numGlyphs</c>).</summary>
    public int GlyphCount { get; }

    /// <summary>The <c>head.flags</c> field.</summary>
    public ushort HeadFlags { get; }

    /// <summary>The <c>head.macStyle</c> field (bit 0: bold, bit 1: italic).</summary>
    public ushort MacStyle { get; }

    /// <summary>The preferred family name (the typographic family name, else the family name).</summary>
    public string FamilyName { get; }

    /// <summary>The preferred subfamily name (e.g. "Bold Italic").</summary>
    public string SubfamilyName { get; }

    /// <summary>The full name of the font (used by <c>local()</c>).</summary>
    public string FullName { get; }

    /// <summary>The PostScript name of the font (used by <c>local()</c>).</summary>
    public string PostScriptName { get; }

    /// <summary>The ascender of the hhea table.</summary>
    public short HheaAscender { get; }

    /// <summary>The descender of the hhea table (negative below the baseline).</summary>
    public short HheaDescender { get; }

    /// <summary>The line gap of the hhea table.</summary>
    public short HheaLineGap { get; }

    /// <summary>Whether the font has an OS/2 table.</summary>
    public bool HasOS2 { get; }

    /// <summary>The version of the OS/2 table.</summary>
    public ushort OS2Version { get; }

    /// <summary>The average advance width of the glyphs (<c>OS/2.xAvgCharWidth</c>, 0 when there is no OS/2 table), in font units.</summary>
    public short AverageCharWidth { get; }

    /// <summary>The width of the bounding box of all the glyphs (<c>head.xMax - head.xMin</c>), in font units.</summary>
    public int MaxCharWidth { get; }

    /// <summary>The weight class of the font (<c>OS/2.usWeightClass</c>, 400 when there is no OS/2 table).</summary>
    public ushort WeightClass { get; } = 400;

    /// <summary>The width class of the font (<c>OS/2.usWidthClass</c>, from 1 (ultra-condensed) to 9 (ultra-expanded), 5 when there is no OS/2 table).</summary>
    public ushort WidthClass { get; } = 5;

    /// <summary>The <c>OS/2.fsSelection</c> field.</summary>
    public ushort FsSelection { get; }

    /// <summary>The typographic ascender of the OS/2 table.</summary>
    public short TypoAscender { get; }

    /// <summary>The typographic descender of the OS/2 table (negative below the baseline).</summary>
    public short TypoDescender { get; }

    /// <summary>The typographic line gap of the OS/2 table.</summary>
    public short TypoLineGap { get; }

    /// <summary>The Windows ascender of the OS/2 table.</summary>
    public ushort WinAscent { get; }

    /// <summary>The Windows descender of the OS/2 table (positive below the baseline).</summary>
    public ushort WinDescent { get; }

    /// <summary>The x-height of the OS/2 table (version 2 and later), 0 when it is not defined.</summary>
    public short OS2XHeight { get; }

    /// <summary>The cap height of the OS/2 table (version 2 and later), 0 when it is not defined.</summary>
    public short OS2CapHeight { get; }

    /// <summary>The vertical offset of the subscripts of the OS/2 table (positive below the baseline).</summary>
    public short SubscriptYOffset { get; }

    /// <summary>The vertical offset of the superscripts of the OS/2 table.</summary>
    public short SuperscriptYOffset { get; }

    /// <summary>The thickness of the strikeout stroke of the OS/2 table.</summary>
    public short StrikeoutSize { get; }

    /// <summary>The position of the top of the strikeout stroke of the OS/2 table.</summary>
    public short StrikeoutPosition { get; }

    /// <summary>The italic angle of the post table, in degrees counter-clockwise from the vertical.</summary>
    public double ItalicAngle { get; }

    /// <summary>The position of the top of the underline of the post table.</summary>
    public short UnderlinePosition { get; }

    /// <summary>The thickness of the underline of the post table.</summary>
    public short UnderlineThickness { get; }

    /// <summary>Whether the font is monospaced (<c>post.isFixedPitch</c>).</summary>
    public bool IsFixedPitch { get; }

    /// <summary>Whether the font has a vhea table and a vmtx table.</summary>
    public bool HasVerticalMetrics { get; }

    /// <summary>The vertical typographic ascender of the vhea table.</summary>
    public short VerticalAscender { get; }

    /// <summary>The vertical typographic descender of the vhea table.</summary>
    public short VerticalDescender { get; }

    /// <summary>The vertical typographic line gap of the vhea table.</summary>
    public short VerticalLineGap { get; }

    /// <summary>Whether the OS/2 table requests the typographic metrics (<c>USE_TYPO_METRICS</c>, bit 7 of fsSelection).</summary>
    public bool UseTypoMetrics => HasOS2 && (FsSelection & 0x80) != 0;

    /// <summary>Whether the font is italic or oblique (<c>OS/2.fsSelection</c> bits 0 and 9, or <c>head.macStyle</c> bit 1).</summary>
    public bool IsItalic => (FsSelection & 0x0201) != 0 || (MacStyle & 0x02) != 0;

    /// <summary>Whether the font is oblique rather than italic (<c>OS/2.fsSelection</c> bit 9).</summary>
    public bool IsOblique => (FsSelection & 0x0200) != 0;

    /// <summary>Whether the font is bold (<c>OS/2.fsSelection</c> bit 5, or <c>head.macStyle</c> bit 0).</summary>
    public bool IsBold => (FsSelection & 0x20) != 0 || (MacStyle & 0x01) != 0;

    /// <summary>The <c>font-width</c> of the width class, as a percentage (https://drafts.csswg.org/css-fonts-4/#font-stretch-prop).</summary>
    public double WidthPercentage => WidthClass switch
    {
        1 => 50,
        2 => 62.5,
        3 => 75,
        4 => 87.5,
        6 => 112.5,
        7 => 125,
        8 => 150,
        9 => 200,
        _ => 100,
    };

    /// <summary>The variation axes, named instances and variation tables of the font (fvar, avar, HVAR, MVAR).</summary>
    public FontVariations Variations { get; }

    /// <summary>The metrics of the default instance of the font.</summary>
    public FontMetrics Metrics => GetMetrics(instance: null);

    /// <summary>Creates a face of a font file from its table directory; returns null when the face is invalid.</summary>
    internal static OpenTypeFont? TryCreate(FontFile file, long directoryOffset, int faceIndex)
    {
        // https://learn.microsoft.com/typography/opentype/spec/otff#table-directory
        var data = file.Data;
        if (!FontBinary.IsInRange(data, directoryOffset, 12))
            return null;

        var offset = (int)directoryOffset;
        var version = FontBinary.U32(data, offset);
        var tableCount = FontBinary.U16(data, offset + 4);
        if (version is not (OpenTypeTag.TrueTypeVersion or OpenTypeTag.OpenTypeCff or OpenTypeTag.AppleTrueType) || tableCount is 0 or > MaxTableCount || !FontBinary.IsInRange(data, offset + 12, tableCount * TableRecordLength))
            return null;

        var tags = new uint[tableCount];
        var offsets = new int[tableCount];
        var lengths = new int[tableCount];
        for (var i = 0; i < tableCount; i++)
        {
            var record = offset + 12 + (i * TableRecordLength);
            var tableOffset = FontBinary.U32(data, record + 8);
            var tableLength = FontBinary.U32(data, record + 12);
            if (!FontBinary.IsInRange(data, tableOffset, tableLength))
                return null;

            tags[i] = FontBinary.U32(data, record);
            offsets[i] = (int)tableOffset;
            lengths[i] = (int)tableLength;
        }

        if (!HasValidRequiredTables(data, tags, offsets, lengths))
            return null;

        return new OpenTypeFont(file, faceIndex, tags, offsets, lengths);
    }

    /// <summary>
    /// Validates the tables that the layout needs: head, hhea, maxp, hmtx and cmap, and the outlines (glyf and loca, CFF,
    /// CFF2, or color bitmaps or SVG glyphs), as the OpenType sanitizer of the browsers requires them.
    /// </summary>
    private static bool HasValidRequiredTables(byte[] data, uint[] tags, int[] offsets, int[] lengths)
    {
        ReadOnlySpan<byte> Find(uint tag)
        {
            var index = Array.IndexOf(tags, tag);
            return index < 0 ? default : data.AsSpan(offsets[index], lengths[index]);
        }

        bool Has(uint tag) => Array.IndexOf(tags, tag) >= 0;

        // https://learn.microsoft.com/typography/opentype/spec/head: the magic number, the units per em and the format of loca
        var head = Find(OpenTypeTag.Head);
        if (head.Length < 54 || FontBinary.U32(head, 12) != 0x5F0F3CF5 || FontBinary.U16(head, 18) is < 16 or > 16384 || FontBinary.I16(head, 50) is not (0 or 1))
            return false;

        var maxp = Find(OpenTypeTag.Maxp);
        var glyphCount = FontBinary.U16(maxp, 4);
        if (maxp.Length < 6 || glyphCount == 0)
            return false;

        // https://learn.microsoft.com/typography/opentype/spec/hmtx: the long metrics are required, the left side bearings of
        // the other glyphs may be truncated (they are 0)
        var hhea = Find(OpenTypeTag.Hhea);
        var metricsCount = Math.Min(FontBinary.U16(hhea, 34), glyphCount);
        if (hhea.Length < 36 || metricsCount == 0 || Find(OpenTypeTag.Hmtx).Length < metricsCount * 4)
            return false;

        if (Find(OpenTypeTag.Cmap).Length < 4)
            return false;

        if (Has(OpenTypeTag.Glyf) || Has(OpenTypeTag.Loca))
        {
            var locaLength = (glyphCount + 1) * (FontBinary.I16(head, 50) == 1 ? 4 : 2);
            return Has(OpenTypeTag.Glyf) && Find(OpenTypeTag.Loca).Length >= locaLength;
        }

        return Has(OpenTypeTag.Cff) || Has(OpenTypeTag.Cff2) || Has(OpenTypeTag.Cbdt) || Has(OpenTypeTag.Sbix) || Has(OpenTypeTag.Svg);
    }

    /// <summary>Returns whether the font has a table.</summary>
    public bool HasTable(uint tag) => Array.IndexOf(_tableTags, tag) >= 0;

    /// <summary>The data of a table, or an empty span when the font does not have it.</summary>
    public ReadOnlySpan<byte> GetTable(uint tag)
    {
        var (offset, length) = GetTableRange(tag);
        return offset < 0 ? default : Data.AsSpan(offset, length);
    }

    private (int Offset, int Length) GetTableRange(uint tag)
    {
        var index = Array.IndexOf(_tableTags, tag);
        return index < 0 ? (-1, 0) : (_tableOffsets[index], _tableLengths[index]);
    }

    private OpenTypeMath? _math;
    private bool _mathParsed;

    /// <summary>The OpenType MATH table of the font (https://learn.microsoft.com/typography/opentype/spec/math), or null when it has none.</summary>
    public OpenTypeMath? MathTable
    {
        get
        {
            if (!_mathParsed)
            {
                // The table is immutable: a concurrent read creates an equivalent instance
                _math = OpenTypeMath.Create(this, GetTableRange(OpenTypeTag.Math));
                _mathParsed = true;
            }

            return _math;
        }
    }

    private OpenTypeColorGlyphs? _colorGlyphs;
    private bool _colorGlyphsParsed;

    /// <summary>
    /// The color glyphs of the font: the layers of the COLR table and the palettes of the CPAL table, or null when the font
    /// has no glyph made of colored layers.
    /// </summary>
    public OpenTypeColorGlyphs? ColorGlyphs
    {
        get
        {
            if (!_colorGlyphsParsed)
            {
                // The tables are immutable: a concurrent read creates an equivalent instance
                _colorGlyphs = OpenTypeColorGlyphs.Create(Data, GetTableRange(OpenTypeTag.Colr), GetTableRange(OpenTypeTag.Cpal));
                _colorGlyphsParsed = true;
            }

            return _colorGlyphs;
        }
    }

    /// <summary>The tags of the tables of the font.</summary>
    public IReadOnlyList<uint> TableTags => _tableTags;

    /// <summary>
    /// The metrics of an instance of the font (the default instance when it is null), with the deltas of the MVAR table.
    /// The ascent, descent and line gap are the typographic metrics of the OS/2 table when <c>USE_TYPO_METRICS</c> is set,
    /// else the metrics of the hhea table (the typographic metrics, then the Windows metrics, when they are 0), like the
    /// browsers (https://drafts.csswg.org/css-inline-3/#ascent-descent).
    /// </summary>
    public FontMetrics GetMetrics(FontVariationInstance? instance)
    {
        double Delta(uint tag) => instance is null ? 0 : Variations.GetMetricsDelta(tag, instance);

        double ascent, descent, lineGap;
        if (UseTypoMetrics || (HheaAscender == 0 && HheaDescender == 0 && HasOS2 && (TypoAscender != 0 || TypoDescender != 0)))
        {
            ascent = TypoAscender + Delta(FontVariations.MetricsTag.HorizontalAscender);
            descent = -TypoDescender - Delta(FontVariations.MetricsTag.HorizontalDescender);
            lineGap = TypoLineGap + Delta(FontVariations.MetricsTag.HorizontalLineGap);
        }
        else if (HheaAscender != 0 || HheaDescender != 0 || !HasOS2)
        {
            ascent = HheaAscender;
            descent = -HheaDescender;
            lineGap = HheaLineGap;
        }
        else
        {
            ascent = WinAscent + Delta(FontVariations.MetricsTag.HorizontalClippingAscent);
            descent = WinDescent + Delta(FontVariations.MetricsTag.HorizontalClippingDescent);
            lineGap = 0;
        }

        // The x-height and the cap height of the OS/2 table, else the top of the outline of x and H
        double? xHeight = OS2XHeight > 0 ? OS2XHeight : TryGetGlyphBounds(GetGlyphId('x'), out var x) ? x.YMax : null;
        double? capHeight = OS2CapHeight > 0 ? OS2CapHeight : TryGetGlyphBounds(GetGlyphId('H'), out var h) ? h.YMax : null;
        return new FontMetrics
        {
            Ascent = ascent,
            Descent = descent,
            LineGap = lineGap,
            XHeight = xHeight + Delta(FontVariations.MetricsTag.XHeight),
            CapHeight = capHeight + Delta(FontVariations.MetricsTag.CapHeight),
            UnderlinePosition = UnderlinePosition + Delta(FontVariations.MetricsTag.UnderlineOffset),
            UnderlineThickness = UnderlineThickness + Delta(FontVariations.MetricsTag.UnderlineSize),
            StrikeoutPosition = StrikeoutPosition + Delta(FontVariations.MetricsTag.StrikeoutOffset),
            StrikeoutThickness = StrikeoutSize + Delta(FontVariations.MetricsTag.StrikeoutSize),
            SubscriptOffset = SubscriptYOffset + Delta(FontVariations.MetricsTag.SubscriptYOffset),
            SuperscriptOffset = SuperscriptYOffset + Delta(FontVariations.MetricsTag.SuperscriptYOffset),
        };
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Horizontal and vertical metrics of the glyphs
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The advance width of a glyph in font units (https://learn.microsoft.com/typography/opentype/spec/hmtx).</summary>
    public int GetAdvanceWidth(ushort glyph)
    {
        // The glyphs after the last long metric have the advance of the last one
        var index = Math.Min(glyph, _horizontalMetricsCount - 1);
        return FontBinary.U16(Data.AsSpan(_hmtxOffset, _hmtxLength), index * 4);
    }

    /// <summary>The advance width of a glyph in an instance of a variable font (with the deltas of the HVAR table).</summary>
    public double GetAdvanceWidth(ushort glyph, FontVariationInstance? instance)
    {
        var advance = GetAdvanceWidth(glyph);
        return instance is null || instance.IsDefault ? advance : advance + Variations.GetAdvanceWidthDelta(glyph, instance);
    }

    /// <summary>The left side bearing of a glyph in font units.</summary>
    public int GetLeftSideBearing(ushort glyph)
    {
        var hmtx = Data.AsSpan(_hmtxOffset, _hmtxLength);
        return glyph < _horizontalMetricsCount ? FontBinary.I16(hmtx, (glyph * 4) + 2) : FontBinary.I16(hmtx, (_horizontalMetricsCount * 4) + ((glyph - _horizontalMetricsCount) * 2));
    }

    /// <summary>
    /// The advance height of a glyph in font units (https://learn.microsoft.com/typography/opentype/spec/vmtx). A font
    /// without vertical metrics has the advance of its ascent and descent, as in the browsers.
    /// </summary>
    public int GetAdvanceHeight(ushort glyph)
    {
        if (!HasVerticalMetrics)
        {
            var metrics = Metrics;
            return (int)Math.Round(metrics.Ascent + metrics.Descent, MidpointRounding.AwayFromZero);
        }

        var index = Math.Min(glyph, _verticalMetricsCount - 1);
        return FontBinary.U16(Data.AsSpan(_vmtxOffset, _vmtxLength), index * 4);
    }

    /// <summary>The top side bearing of a glyph in font units, or 0 when the font has no vertical metrics.</summary>
    public int GetTopSideBearing(ushort glyph)
    {
        if (!HasVerticalMetrics)
            return 0;

        var vmtx = Data.AsSpan(_vmtxOffset, _vmtxLength);
        return glyph < _verticalMetricsCount ? FontBinary.I16(vmtx, (glyph * 4) + 2) : FontBinary.I16(vmtx, (_verticalMetricsCount * 4) + ((glyph - _verticalMetricsCount) * 2));
    }

    /// <summary>
    /// The bounding box of the outline of a glyph: from the header of the glyph in the glyf table of a TrueType font
    /// (https://learn.microsoft.com/typography/opentype/spec/glyf#glyph-headers), or computed from the charstring of a font with
    /// CFF outlines; returns false when the glyph has no outline (or the font has CFF2 outlines).
    /// </summary>
    public bool TryGetGlyphBounds(ushort glyph, out GlyphBounds bounds)
    {
        bounds = default;
        if (_glyfOffset < 0 || _locaOffset < 0)
            return TryGetCffGlyphBounds(glyph, out bounds);

        if (glyph >= GlyphCount)
            return false;

        var loca = Data.AsSpan(_locaOffset, _locaLength);
        long start, end;
        if (_isLongLoca)
        {
            start = FontBinary.U32(loca, glyph * 4);
            end = FontBinary.U32(loca, (glyph + 1) * 4);
        }
        else
        {
            start = FontBinary.U16(loca, glyph * 2) * 2L;
            end = FontBinary.U16(loca, (glyph + 1) * 2) * 2L;
        }

        if (end - start < 10 || end > _glyfLength)
            return false;

        var header = Data.AsSpan(_glyfOffset + (int)start, 10);
        bounds = new GlyphBounds(FontBinary.I16(header, 2), FontBinary.I16(header, 4), FontBinary.I16(header, 6), FontBinary.I16(header, 8));
        return true;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Character to glyph mapping
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Selects the Unicode subtable of the cmap table, and the Unicode Variation Sequences subtable (format 14).</summary>
    private static (int Subtable, ushort Format, bool IsSymbol, int VariationSubtable) SelectCmapSubtables(byte[] data, int cmapOffset)
    {
        if (cmapOffset < 0)
            return (-1, 0, false, -1);

        var cmap = data.AsSpan(cmapOffset);
        var count = FontBinary.U16(cmap, 2);
        var best = -1;
        var bestScore = 0;
        ushort bestFormat = 0;
        var isSymbol = false;
        var variations = -1;
        for (var i = 0; i < count; i++)
        {
            var record = 4 + (i * 8);
            var platform = FontBinary.U16(cmap, record);
            var encoding = FontBinary.U16(cmap, record + 2);
            var offset = FontBinary.U32(cmap, record + 4);
            if (!FontBinary.IsInRange(cmap, offset, 4))
                continue;

            var format = FontBinary.U16(cmap, (int)offset);
            if (format == 14)
            {
                if (platform == 0 && encoding == 5)
                {
                    variations = cmapOffset + (int)offset;
                }

                continue;
            }

            if (format is not (0 or 4 or 6 or 10 or 12 or 13))
                continue;

            // The full repertoire subtables first, then the BMP ones, then the symbol and Macintosh ones
            var score = (platform, encoding) switch
            {
                (3, 10) => 10,
                (0, 6) => 9,
                (0, 4) => 8,
                (3, 1) => 7,
                (0, 3) => 6,
                (0, _) => 5,
                (3, 0) => 2,
                (1, 0) => 1,
                _ => 0,
            };

            // A subtable for the full repertoire must have a format that supports it
            if (score is 10 or 9 or 8 && format is not (10 or 12 or 13))
            {
                score = 4;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = cmapOffset + (int)offset;
                bestFormat = format;
                isSymbol = platform == 3 && encoding == 0;
            }
        }

        return (best, bestFormat, isSymbol, variations);
    }

    /// <summary>The glyph of a character (https://learn.microsoft.com/typography/opentype/spec/cmap), 0 (the .notdef glyph) when the font does not map it.</summary>
    public ushort GetGlyphId(int codePoint)
    {
        if (_cmapSubtable < 0 || codePoint < 0)
            return 0;

        // A direct-mapped cache of the last code points looked up (each entry is written atomically: the code point plus 1,
        // then the glyph; the faces are shared by the threads, a race only loses an entry)
        var cache = _glyphCache;
        ref var entry = ref cache[codePoint & (GlyphCacheSize - 1)];
        var cached = Volatile.Read(ref entry);
        if ((int)(cached >> 16) == codePoint + 1)
            return (ushort)cached;

        var glyph = LookupCmap(Data.AsSpan(_cmapSubtable), _cmapFormat, codePoint);

        // The characters of a symbol font are mapped to the private use area U+F000-F0FF (as in the browsers)
        if (glyph == 0 && _isSymbolCmap && codePoint <= 0xFF)
        {
            glyph = LookupCmap(Data.AsSpan(_cmapSubtable), _cmapFormat, codePoint + 0xF000);
        }

        glyph = glyph < GlyphCount ? glyph : (ushort)0;
        Volatile.Write(ref entry, ((ulong)(codePoint + 1) << 16) | glyph);
        return glyph;
    }

    private const int GlyphCacheSize = 512;
    private readonly ulong[] _glyphCache = new ulong[GlyphCacheSize];

    /// <summary>Returns whether the font maps a character to a glyph.</summary>
    public bool HasGlyph(int codePoint) => GetGlyphId(codePoint) != 0;

    /// <summary>
    /// The glyph of a variation sequence (a character followed by a variation selector) of the Unicode Variation Sequences
    /// subtable (https://learn.microsoft.com/typography/opentype/spec/cmap#format-14-unicode-variation-sequences); returns
    /// false when the font does not support the sequence (the variation selector is then ignored).
    /// </summary>
    public bool TryGetGlyphId(int codePoint, int variationSelector, out ushort glyph)
    {
        glyph = 0;
        if (_cmapVariationSubtable < 0)
            return false;

        var subtable = Data.AsSpan(_cmapVariationSubtable);
        var recordCount = FontBinary.U32(subtable, 6);
        if (!FontBinary.IsInRange(subtable, 10, recordCount * 11L))
            return false;

        // The records are sorted by variation selector
        int low = 0, high = (int)recordCount - 1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            var record = 10 + (middle * 11);
            var selector = (int)FontBinary.U24(subtable, record);
            if (selector < variationSelector)
            {
                low = middle + 1;
            }
            else if (selector > variationSelector)
            {
                high = middle - 1;
            }
            else
            {
                // The default UVS table: the sequence has the glyph of the character
                var defaultOffset = FontBinary.U32(subtable, record + 3);
                if (defaultOffset != 0 && IsInDefaultUvs(FontBinary.Slice(subtable, defaultOffset), codePoint))
                {
                    glyph = GetGlyphId(codePoint);
                    return glyph != 0;
                }

                var nonDefaultOffset = FontBinary.U32(subtable, record + 7);
                if (nonDefaultOffset != 0 && FindNonDefaultUvs(FontBinary.Slice(subtable, nonDefaultOffset), codePoint) is { } result && result < GlyphCount)
                {
                    glyph = result;
                    return true;
                }

                return false;
            }
        }

        return false;

        static bool IsInDefaultUvs(ReadOnlySpan<byte> table, int codePoint)
        {
            var count = FontBinary.U32(table, 0);
            int low = 0, high = (int)Math.Min(count, (uint)(table.Length / 4)) - 1;
            while (low <= high)
            {
                var middle = (low + high) >>> 1;
                var start = (int)FontBinary.U24(table, 4 + (middle * 4));
                var end = start + FontBinary.U8(table, 4 + (middle * 4) + 3);
                if (codePoint < start)
                {
                    high = middle - 1;
                }
                else if (codePoint > end)
                {
                    low = middle + 1;
                }
                else
                {
                    return true;
                }
            }

            return false;
        }

        static ushort? FindNonDefaultUvs(ReadOnlySpan<byte> table, int codePoint)
        {
            var count = FontBinary.U32(table, 0);
            int low = 0, high = (int)Math.Min(count, (uint)(table.Length / 5)) - 1;
            while (low <= high)
            {
                var middle = (low + high) >>> 1;
                var value = (int)FontBinary.U24(table, 4 + (middle * 5));
                if (codePoint < value)
                {
                    high = middle - 1;
                }
                else if (codePoint > value)
                {
                    low = middle + 1;
                }
                else
                {
                    return FontBinary.U16(table, 4 + (middle * 5) + 3);
                }
            }

            return null;
        }
    }

    private static ushort LookupCmap(ReadOnlySpan<byte> subtable, ushort format, int codePoint)
    {
        switch (format)
        {
            case 0:
                // https://learn.microsoft.com/typography/opentype/spec/cmap#format-0-byte-encoding-table
                return codePoint < 256 ? FontBinary.U8(subtable, 6 + codePoint) : (ushort)0;

            case 4:
                return LookupFormat4(subtable, codePoint);

            case 6:
            {
                // https://learn.microsoft.com/typography/opentype/spec/cmap#format-6-trimmed-table-mapping
                var first = FontBinary.U16(subtable, 6);
                var count = FontBinary.U16(subtable, 8);
                return codePoint >= first && codePoint < first + count ? FontBinary.U16(subtable, 10 + ((codePoint - first) * 2)) : (ushort)0;
            }

            case 10:
            {
                // https://learn.microsoft.com/typography/opentype/spec/cmap#format-10-trimmed-array
                var first = FontBinary.U32(subtable, 12);
                var count = FontBinary.U32(subtable, 16);
                return (uint)codePoint >= first && (uint)codePoint - first < count ? FontBinary.U16(subtable, (int)Math.Min(20 + (((uint)codePoint - first) * 2L), int.MaxValue)) : (ushort)0;
            }

            case 12:
            case 13:
            {
                // https://learn.microsoft.com/typography/opentype/spec/cmap#format-12-segmented-coverage: the groups are sorted
                var count = FontBinary.U32(subtable, 12);
                int low = 0, high = (int)Math.Min(count, (uint)Math.Max(0, (subtable.Length - 16) / 12)) - 1;
                while (low <= high)
                {
                    var middle = (low + high) >>> 1;
                    var group = 16 + (middle * 12);
                    var start = FontBinary.U32(subtable, group);
                    var end = FontBinary.U32(subtable, group + 4);
                    if ((uint)codePoint < start)
                    {
                        high = middle - 1;
                    }
                    else if ((uint)codePoint > end)
                    {
                        low = middle + 1;
                    }
                    else
                    {
                        var glyph = FontBinary.U32(subtable, group + 8);

                        // Format 13 maps every character of the group to the same glyph
                        glyph = format == 12 ? glyph + ((uint)codePoint - start) : glyph;
                        return glyph <= ushort.MaxValue ? (ushort)glyph : (ushort)0;
                    }
                }

                return 0;
            }

            default:
                return 0;
        }
    }

    /// <summary>https://learn.microsoft.com/typography/opentype/spec/cmap#format-4-segment-mapping-to-delta-values</summary>
    private static ushort LookupFormat4(ReadOnlySpan<byte> subtable, int codePoint)
    {
        if (codePoint > 0xFFFF)
            return 0;

        var segmentCount = FontBinary.U16(subtable, 6) / 2;
        var endCodes = 14;
        var startCodes = endCodes + (segmentCount * 2) + 2;
        var deltas = startCodes + (segmentCount * 2);
        var rangeOffsets = deltas + (segmentCount * 2);

        // The first segment whose end code is greater than or equal to the character (the segments are sorted)
        int low = 0, high = segmentCount - 1;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (FontBinary.U16(subtable, endCodes + (middle * 2)) < codePoint)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        if (segmentCount == 0 || FontBinary.U16(subtable, endCodes + (low * 2)) < codePoint)
            return 0;

        var startCode = FontBinary.U16(subtable, startCodes + (low * 2));
        if (codePoint < startCode)
            return 0;

        var delta = FontBinary.U16(subtable, deltas + (low * 2));
        var rangeOffsetPosition = rangeOffsets + (low * 2);
        var rangeOffset = FontBinary.U16(subtable, rangeOffsetPosition);
        if (rangeOffset == 0)
            return (ushort)(codePoint + delta);

        var glyph = FontBinary.U16(subtable, rangeOffsetPosition + rangeOffset + ((codePoint - startCode) * 2));
        return glyph == 0 ? (ushort)0 : (ushort)(glyph + delta);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Names
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A name of the name table (https://learn.microsoft.com/typography/opentype/spec/name), or null when the font does not
    /// have it: the English name of the Windows platform is preferred, then the other Windows names, then the Unicode and the
    /// Macintosh Roman names.
    /// </summary>
    public string? GetName(int nameId)
    {
        var name = GetTable(OpenTypeTag.Name);
        var count = FontBinary.U16(name, 2);
        var storage = FontBinary.U16(name, 4);
        var bestScore = 0;
        var bestRecord = -1;
        for (var i = 0; i < count; i++)
        {
            var record = 6 + (i * 12);
            if (FontBinary.U16(name, record + 6) != nameId)
                continue;

            var platform = FontBinary.U16(name, record);
            var encoding = FontBinary.U16(name, record + 2);
            var language = FontBinary.U16(name, record + 4);
            var score = platform switch
            {
                3 when encoding is 0 or 1 or 10 => language == 0x0409 ? 5 : 4,
                0 => 3,
                1 when encoding == 0 => language == 0 ? 2 : 1,
                _ => 0,
            };

            if (score > bestScore)
            {
                bestScore = score;
                bestRecord = record;
            }
        }

        if (bestRecord < 0)
            return null;

        var bytes = FontBinary.Slice(name, storage + FontBinary.U16(name, bestRecord + 10), FontBinary.U16(name, bestRecord + 8));
        return FontBinary.U16(name, bestRecord) == 1 ? DecodeMacRoman(bytes) : Encoding.BigEndianUnicode.GetString(bytes);
    }

    /// <summary>
    /// The names of the family of the font in all the languages of the name table, for the matching of the family names
    /// (https://drafts.csswg.org/css-fonts-4/#localized-name-matching): the typographic family names when the font has some,
    /// else the family names (which then also group the faces by style, e.g. "Family Light"). The names of the Macintosh
    /// platform in another encoding than Roman are ignored.
    /// </summary>
    public IReadOnlyList<string> GetFamilyNames()
    {
        var names = new List<string>();
        AddNames(NameId.TypographicFamily);
        if (names.Count == 0)
        {
            AddNames(NameId.Family);
        }

        return names;

        void AddNames(int nameId)
        {
            var name = GetTable(OpenTypeTag.Name);
            var count = FontBinary.U16(name, 2);
            var storage = FontBinary.U16(name, 4);
            for (var i = 0; i < count; i++)
            {
                var record = 6 + (i * 12);
                if (FontBinary.U16(name, record + 6) != nameId)
                    continue;

                var platform = FontBinary.U16(name, record);
                var encoding = FontBinary.U16(name, record + 2);
                if (!(platform is 0 || (platform == 3 && encoding is 0 or 1 or 10) || (platform == 1 && encoding == 0)))
                    continue;

                var bytes = FontBinary.Slice(name, storage + FontBinary.U16(name, record + 10), FontBinary.U16(name, record + 8));
                var value = platform == 1 ? DecodeMacRoman(bytes) : Encoding.BigEndianUnicode.GetString(bytes);
                if (value.Length > 0 && !names.Contains(value, StringComparer.Ordinal))
                {
                    names.Add(value);
                }
            }
        }
    }

    /// <summary>Decodes a Macintosh Roman string (https://www.unicode.org/Public/MAPPINGS/VENDORS/APPLE/ROMAN.TXT).</summary>
    private static string DecodeMacRoman(ReadOnlySpan<byte> bytes)
    {
        const string HighHalf = "\U000000C4\U000000C5\U000000C7\U000000C9\U000000D1\U000000D6\U000000DC\U000000E1\U000000E0\U000000E2\U000000E4\U000000E3\U000000E5\U000000E7\U000000E9\U000000E8\U000000EA\U000000EB\U000000ED\U000000EC\U000000EE\U000000EF\U000000F1\U000000F3\U000000F2\U000000F4\U000000F6\U000000F5\U000000FA\U000000F9\U000000FB\U000000FC"
            + "\U00002020\U000000B0\U000000A2\U000000A3\U000000A7\U00002022\U000000B6\U000000DF\U000000AE\U000000A9\U00002122\U000000B4\U000000A8\U00002260\U000000C6\U000000D8\U0000221E\U000000B1\U00002264\U00002265\U000000A5\U000000B5\U00002202\U00002211\U0000220F\U000003C0\U0000222B\U000000AA\U000000BA\U000003A9\U000000E6\U000000F8"
            + "\U000000BF\U000000A1\U000000AC\U0000221A\U00000192\U00002248\U00002206\U000000AB\U000000BB\U00002026\U000000A0\U000000C0\U000000C3\U000000D5\U00000152\U00000153\U00002013\U00002014\U0000201C\U0000201D\U00002018\U00002019\U000000F7\U000025CA\U000000FF\U00000178\U00002044\U000020AC\U00002039\U0000203A\U0000FB01\U0000FB02"
            + "\U00002021\U000000B7\U0000201A\U0000201E\U00002030\U000000C2\U000000CA\U000000C1\U000000CB\U000000C8\U000000CD\U000000CE\U000000CF\U000000CC\U000000D3\U000000D4\U0000F8FF\U000000D2\U000000DA\U000000DB\U000000D9\U00000131\U000002C6\U000002DC\U000000AF\U000002D8\U000002D9\U000002DA\U000000B8\U000002DD\U000002DB\U000002C7";
        var result = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            result.Append(b < 0x80 ? (char)b : HighHalf[b - 0x80]);
        }

        return result.ToString();
    }

    /// <inheritdoc />
    public override string ToString() => $"{FullName} ({File.Format})";
}
