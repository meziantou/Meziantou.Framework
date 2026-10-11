namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The glyph definition table: OpenType specification, "GDEF - Glyph Definition Table". It gives the class of each
/// glyph (base, ligature, mark, component), the attachment class of the marks, and sets of marks, which the lookups of
/// the other layout tables use to choose the glyphs they skip.
/// </summary>
/// <remarks>The header is validated when the table is read. The attachment points and the ligature carets are not used.</remarks>
internal sealed class GdefTable
{
    private readonly int _glyphClasses;
    private readonly int _markAttachmentClasses;
    private readonly int _markGlyphSets;

    private GdefTable(int glyphClasses, int markAttachmentClasses, int markGlyphSets)
    {
        _glyphClasses = glyphClasses;
        _markAttachmentClasses = markAttachmentClasses;
        _markGlyphSets = markGlyphSets;
    }

    /// <summary>Gets a value indicating whether the table gives the classes of the glyphs.</summary>
    public bool HasGlyphClasses => _glyphClasses != 0;

    /// <summary>Reads the header of the table.</summary>
    /// <returns>The table, or <see langword="null"/> when its version is not known: the font is then used without it.</returns>
    public static GdefTable? Read(ReadOnlySpan<byte> table)
    {
        if (table.Length < 12 || FontBinary.UInt16(table, 0) != 1)
            return null;

        var minor = FontBinary.UInt16(table, 2);
        var glyphClasses = Validate(table, FontBinary.UInt16(table, 4));
        var markAttachmentClasses = Validate(table, FontBinary.UInt16(table, 10));
        var markGlyphSets = minor >= 2 && table.Length >= 14 ? Validate(table, FontBinary.UInt16(table, 12)) : 0;
        return new GdefTable(glyphClasses, markAttachmentClasses, markGlyphSets);
    }

    /// <summary>Gets the class of a glyph as the bits of <see cref="GlyphInfo"/>, or 0 for a glyph without class.</summary>
    public int GetGlyphClass(ReadOnlySpan<byte> table, int glyph)
    {
        if (_glyphClasses == 0)
            return 0;

        // 1: base, 2: ligature, 3: mark, 4: component (which no lookup flag names).
        return LayoutCommon.GetClass(table, _glyphClasses, glyph) switch
        {
            1 => GlyphInfo.BaseGlyph,
            2 => GlyphInfo.LigatureGlyph,
            3 => GlyphInfo.MarkGlyph,
            _ => 0,
        };
    }

    /// <summary>Gets the attachment class of a mark; 0 when the table has none.</summary>
    public int GetMarkAttachmentClass(ReadOnlySpan<byte> table, int glyph)
        => _markAttachmentClasses == 0 ? 0 : LayoutCommon.GetClass(table, _markAttachmentClasses, glyph) & 0xFF;

    /// <summary>Determines whether a glyph is in a mark glyph set.</summary>
    public bool IsInMarkGlyphSet(ReadOnlySpan<byte> table, int set, int glyph)
    {
        // The sets: a format (1), a count, then 32-bit offsets of coverage tables.
        if (_markGlyphSets == 0 || FontBinary.UInt16(table, _markGlyphSets) != 1 || set >= FontBinary.UInt16(table, _markGlyphSets + 2))
            return false;

        var coverage = FontBinary.Offset32(table, _markGlyphSets + 4 + (set * 4));
        return coverage != 0 && LayoutCommon.GetCoverage(table, FontBinary.AddOffset(_markGlyphSets, coverage), glyph) != LayoutCommon.NotCovered;
    }

    private static int Validate(ReadOnlySpan<byte> table, int offset) => offset != 0 && offset <= table.Length - 4 ? offset : 0;
}
