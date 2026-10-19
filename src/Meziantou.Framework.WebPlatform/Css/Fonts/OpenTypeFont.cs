using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SharedFontException = Meziantou.Framework.Fonts.FontException;
using SharedFontFace = Meziantou.Framework.Fonts.FontFace;
using SharedGlyphScratch = Meziantou.Framework.Fonts.GlyphScratch;
using SharedOutlineKind = Meziantou.Framework.Fonts.FontOutlineKind;
using SharedVariation = Meziantou.Framework.Fonts.CmapVariation;

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
/// A font face of a <see cref="FontFile"/>, read by the font reader shared with Meziantou.Framework.Imaging
/// (<see cref="SharedFontFace"/>): this type adds what CSS needs (the metrics of the browsers, the family names, the
/// style) and gives the glyph ids as 16-bit values. The required tables and the structure of the character map are
/// validated when the file is loaded; a malformed glyph, or a malformed table that is read for a glyph, gives no value,
/// never an exception.
/// </summary>
internal sealed partial class OpenTypeFont
{
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

    // The storage of the decoding of the outlines whose box is computed (each thread has its own)
    [ThreadStatic]
    private static SharedGlyphScratch? s_scratch;

    private readonly uint[] _tableTags;
    private ConcurrentDictionary<ushort, GlyphBounds?>? _outlineBounds;

    internal OpenTypeFont(FontFile file, int faceIndex, SharedFontFace face)
    {
        File = file;
        FaceIndex = faceIndex;
        Face = face;
        _tableTags = face.TableTags.ToArray();
        FamilyName = face.FamilyName;
        SubfamilyName = face.SubfamilyName;
        FullName = face.GetName(NameId.FullName) ?? "";
        PostScriptName = face.PostScriptName ?? "";
        Variations = new FontVariations(face);
    }

    /// <summary>The font file of the face.</summary>
    public FontFile File { get; }

    /// <summary>The index of the face in its file (a collection may have several faces).</summary>
    public int FaceIndex { get; }

    /// <summary>The face of the font reader (the default instance of a variable font).</summary>
    internal SharedFontFace Face { get; }

    /// <summary>The sfnt data of the file (shared by the faces of a collection).</summary>
    public byte[] Data => File.Data;

    /// <summary>The number of font units per em (<c>head.unitsPerEm</c>).</summary>
    public int UnitsPerEm => Face.UnitsPerEm;

    /// <summary>The number of glyphs (<c>maxp.numGlyphs</c>).</summary>
    public int GlyphCount => Face.GlyphCount;

    /// <summary>The <c>head.flags</c> field.</summary>
    public ushort HeadFlags => (ushort)Face.HeadFlags;

    /// <summary>The <c>head.macStyle</c> field (bit 0: bold, bit 1: italic).</summary>
    public ushort MacStyle => (ushort)Face.MacStyle;

    /// <summary>The preferred family name (the typographic family name, else the family name).</summary>
    public string FamilyName { get; }

    /// <summary>The preferred subfamily name (e.g. "Bold Italic").</summary>
    public string SubfamilyName { get; }

    /// <summary>The full name of the font (used by <c>local()</c>).</summary>
    public string FullName { get; }

    /// <summary>The PostScript name of the font (used by <c>local()</c>).</summary>
    public string PostScriptName { get; }

    /// <summary>The ascender of the hhea table.</summary>
    public short HheaAscender => (short)Face.HorizontalAscender;

    /// <summary>The descender of the hhea table (negative below the baseline).</summary>
    public short HheaDescender => (short)Face.HorizontalDescender;

    /// <summary>The line gap of the hhea table.</summary>
    public short HheaLineGap => (short)Face.HorizontalLineGap;

    /// <summary>Whether the font has an OS/2 table.</summary>
    public bool HasOS2 => Face.HasOs2;

    /// <summary>The version of the OS/2 table.</summary>
    public ushort OS2Version => (ushort)Face.Os2Version;

    /// <summary>The average advance width of the glyphs (<c>OS/2.xAvgCharWidth</c>, 0 when there is no OS/2 table), in font units.</summary>
    public short AverageCharWidth => (short)Face.AverageCharWidth;

    /// <summary>The width of the bounding box of all the glyphs (<c>head.xMax - head.xMin</c>), in font units.</summary>
    public int MaxCharWidth => Face.XMax - Face.XMin;

    /// <summary>The weight class of the font (<c>OS/2.usWeightClass</c>, 400 when there is no OS/2 table).</summary>
    public ushort WeightClass => Face.HasOs2 ? (ushort)Face.Weight : (ushort)400;

    /// <summary>The width class of the font (<c>OS/2.usWidthClass</c>, from 1 (ultra-condensed) to 9 (ultra-expanded), 5 when there is no OS/2 table).</summary>
    public ushort WidthClass => (ushort)Face.WidthClass;

    /// <summary>The <c>OS/2.fsSelection</c> field.</summary>
    public ushort FsSelection => (ushort)Face.Selection;

    /// <summary>The typographic ascender of the OS/2 table.</summary>
    public short TypoAscender => (short)Face.TypoAscender;

    /// <summary>The typographic descender of the OS/2 table (negative below the baseline).</summary>
    public short TypoDescender => (short)Face.TypoDescender;

    /// <summary>The typographic line gap of the OS/2 table.</summary>
    public short TypoLineGap => (short)Face.TypoLineGap;

    /// <summary>The Windows ascender of the OS/2 table.</summary>
    public ushort WinAscent => (ushort)Face.WinAscent;

    /// <summary>The Windows descender of the OS/2 table (positive below the baseline).</summary>
    public ushort WinDescent => (ushort)Face.WinDescent;

    /// <summary>The x-height of the OS/2 table (version 2 and later), 0 when it is not defined.</summary>
    public short OS2XHeight => (short)Face.XHeight;

    /// <summary>The cap height of the OS/2 table (version 2 and later), 0 when it is not defined.</summary>
    public short OS2CapHeight => (short)Face.CapHeight;

    /// <summary>The vertical offset of the subscripts of the OS/2 table (positive below the baseline).</summary>
    public short SubscriptYOffset => (short)Face.SubscriptYOffset;

    /// <summary>The vertical offset of the superscripts of the OS/2 table.</summary>
    public short SuperscriptYOffset => (short)Face.SuperscriptYOffset;

    /// <summary>The thickness of the strikeout stroke of the OS/2 table.</summary>
    public short StrikeoutSize => (short)Face.StrikeoutSize;

    /// <summary>The position of the top of the strikeout stroke of the OS/2 table.</summary>
    public short StrikeoutPosition => (short)Face.StrikeoutPosition;

    /// <summary>The italic angle of the post table, in degrees counter-clockwise from the vertical.</summary>
    public double ItalicAngle => Face.ItalicAngle;

    /// <summary>The position of the top of the underline of the post table.</summary>
    public short UnderlinePosition => (short)Face.UnderlinePosition;

    /// <summary>The thickness of the underline of the post table.</summary>
    public short UnderlineThickness => (short)Face.UnderlineThickness;

    /// <summary>Whether the font is monospaced (<c>post.isFixedPitch</c>).</summary>
    public bool IsFixedPitch => Face.IsMonospaced;

    /// <summary>Whether the font has a vhea table and a vmtx table.</summary>
    public bool HasVerticalMetrics => Face.HasVerticalMetrics;

    /// <summary>The vertical typographic ascender of the vhea table.</summary>
    public short VerticalAscender => (short)Face.VerticalAscender;

    /// <summary>The vertical typographic descender of the vhea table.</summary>
    public short VerticalDescender => (short)Face.VerticalDescender;

    /// <summary>The vertical typographic line gap of the vhea table.</summary>
    public short VerticalLineGap => (short)Face.VerticalLineGap;

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

    /// <summary>The variation axes and the named instances of the font.</summary>
    public FontVariations Variations { get; }

    /// <summary>The metrics of the default instance of the font.</summary>
    public FontMetrics Metrics => GetMetrics(instance: null);

    /// <summary>Returns whether the font has a table.</summary>
    public bool HasTable(uint tag) => Array.IndexOf(_tableTags, tag) >= 0;

    /// <summary>The data of a table, or an empty span when the font does not have it.</summary>
    public ReadOnlySpan<byte> GetTable(uint tag) => Face.TryGetTable(tag, out var table) ? table.Span : default;

    private OpenTypeMath? _math;
    private bool _mathParsed;

    /// <summary>The OpenType MATH table of the font (https://learn.microsoft.com/typography/opentype/spec/math), or null when it has none.</summary>
    public OpenTypeMath? MathTable
    {
        get
        {
            if (!_mathParsed)
            {
                _math = Face.HasMathTable ? new OpenTypeMath(Face) : null;
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
                _colorGlyphs = OpenTypeColorGlyphs.Create(Face);
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
        var face = instance?.Face ?? Face;
        double ascent, descent, lineGap;
        if (UseTypoMetrics || (HheaAscender == 0 && HheaDescender == 0 && HasOS2 && (TypoAscender != 0 || TypoDescender != 0)))
        {
            ascent = face.TypoAscender;
            descent = -face.TypoDescender;
            lineGap = face.TypoLineGap;
        }
        else if (HheaAscender != 0 || HheaDescender != 0 || !HasOS2)
        {
            ascent = face.HorizontalAscender;
            descent = -face.HorizontalDescender;
            lineGap = face.HorizontalLineGap;
        }
        else
        {
            ascent = face.WinAscent;
            descent = face.WinDescent;
            lineGap = 0;
        }

        // The x-height and the cap height of the OS/2 table, else the top of the outline of x and H
        double? xHeight = OS2XHeight > 0 ? face.XHeight : TryGetGlyphBounds(GetGlyphId('x'), out var x) ? x.YMax : null;
        double? capHeight = OS2CapHeight > 0 ? face.CapHeight : TryGetGlyphBounds(GetGlyphId('H'), out var h) ? h.YMax : null;
        return new FontMetrics
        {
            Ascent = ascent,
            Descent = descent,
            LineGap = lineGap,
            XHeight = xHeight,
            CapHeight = capHeight,
            UnderlinePosition = face.UnderlinePosition,
            UnderlineThickness = face.UnderlineThickness,
            StrikeoutPosition = face.StrikeoutPosition,
            StrikeoutThickness = face.StrikeoutSize,
            SubscriptOffset = face.SubscriptYOffset,
            SuperscriptOffset = face.SuperscriptYOffset,
        };
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Horizontal and vertical metrics of the glyphs
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The advance width of a glyph in font units (https://learn.microsoft.com/typography/opentype/spec/hmtx).</summary>
    public int GetAdvanceWidth(ushort glyph) => Face.GetAdvanceWidth(glyph);

    /// <summary>
    /// The advance width of a glyph in an instance of a variable font: with the delta of the HVAR table, or, without that
    /// table, the advance of the phantom points of the glyph.
    /// </summary>
    public double GetAdvanceWidth(ushort glyph, FontVariationInstance? instance)
    {
        if (instance is null || instance.IsDefault)
            return GetAdvanceWidth(glyph);

        try
        {
            return instance.Face.GetAdvanceWidth(glyph);
        }
        catch (SharedFontException)
        {
            // A glyph whose variations are malformed has the advance of the default instance
            return GetAdvanceWidth(glyph);
        }
    }

    /// <summary>The left side bearing of a glyph in font units.</summary>
    public int GetLeftSideBearing(ushort glyph) => Face.GetLeftSideBearing(glyph);

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

        return Face.GetAdvanceHeight(glyph);
    }

    /// <summary>The top side bearing of a glyph in font units, or 0 when the font has no vertical metrics.</summary>
    public int GetTopSideBearing(ushort glyph) => Face.GetTopSideBearing(glyph);

    /// <summary>
    /// The bounding box of the outline of a glyph, in the default instance: the box the glyf table declares for the glyph
    /// (https://learn.microsoft.com/typography/opentype/spec/glyf#glyph-headers), or the box of the outline the font reader
    /// decodes (CFF and CFF2 outlines, cached); returns false when the glyph has no outline or its outline is malformed.
    /// </summary>
    public bool TryGetGlyphBounds(ushort glyph, out GlyphBounds bounds)
    {
        if (Face.Outlines == SharedOutlineKind.TrueType)
            return TryComputeGlyphBounds(glyph, out bounds);

        // The outlines of the other formats are decoded: their boxes are kept
        var cache = _outlineBounds ?? Interlocked.CompareExchange(ref _outlineBounds, new ConcurrentDictionary<ushort, GlyphBounds?>(), comparand: null) ?? _outlineBounds;
        if (!cache.TryGetValue(glyph, out var cached))
        {
            cached = TryComputeGlyphBounds(glyph, out var computed) ? computed : null;
            cache.TryAdd(glyph, cached);
        }

        bounds = cached ?? default;
        return cached is not null;
    }

    private bool TryComputeGlyphBounds(ushort glyph, out GlyphBounds bounds)
    {
        bounds = default;
        try
        {
            var scratch = s_scratch ??= new SharedGlyphScratch(FontFile.Environment.CreateAllocator("WebPlatform glyph bounds"));
            if (!Face.TryGetGlyphBounds(glyph, scratch, out var box))
                return false;

            bounds = new GlyphBounds(ToInt16(box.XMin), ToInt16(box.YMin), ToInt16(box.XMax), ToInt16(box.YMax));
            return true;
        }
        catch (SharedFontException)
        {
            return false;
        }

        static short ToInt16(int value) => (short)Math.Clamp(value, short.MinValue, short.MaxValue);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Character to glyph mapping
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The glyph of a character (https://learn.microsoft.com/typography/opentype/spec/cmap), 0 (the .notdef glyph) when the font does not map it.</summary>
    public ushort GetGlyphId(int codePoint) => codePoint < 0 ? (ushort)0 : (ushort)Face.MapCodePoint(codePoint);

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
        switch (Face.MapVariation(codePoint, variationSelector, out var glyphId))
        {
            case SharedVariation.UseDefault:
                // The default UVS table: the sequence has the glyph of the character
                glyph = GetGlyphId(codePoint);
                return glyph != 0;

            case SharedVariation.Glyph:
                glyph = (ushort)glyphId;
                return true;

            default:
                return false;
        }
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
        try
        {
            return Face.GetName(nameId);
        }
        catch (SharedFontException)
        {
            return null;
        }
    }

    /// <summary>
    /// The names of the family of the font in all the languages of the name table, for the matching of the family names
    /// (https://drafts.csswg.org/css-fonts-4/#localized-name-matching): the typographic family names when the font has some,
    /// else the family names (which then also group the faces by style, e.g. "Family Light"). The names of the Macintosh
    /// platform in another encoding than Roman are ignored.
    /// </summary>
    public IReadOnlyList<string> GetFamilyNames()
    {
        try
        {
            var names = Face.GetNames(NameId.TypographicFamily);
            return names.Count != 0 ? names : Face.GetNames(NameId.Family);
        }
        catch (SharedFontException)
        {
            return [];
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"{FullName} ({File.Format})";
}
