namespace Meziantou.Framework.Fonts;

/// <summary>
/// The glyph definition table: OpenType specification, "GDEF - Glyph Definition Table". It gives the class of each
/// glyph (base, ligature, mark, component), the attachment class of the marks, and sets of marks, which the lookups of
/// the other layout tables use to choose the glyphs they skip.
/// </summary>
/// <remarks>
/// The header is validated when the table is read. The positions of the carets inside the ligatures are read for the
/// callers that place a caret in a ligature; the attachment points are not used.
/// </remarks>
internal sealed class GdefTable
{
    private readonly int _glyphClasses;
    private readonly int _markAttachmentClasses;
    private readonly int _markGlyphSets;
    private readonly int _ligatureCarets;

    // The classes of the pages of 256 glyphs that were looked up (see GetGlyphClasses).
    private ushort[]?[]? _classPages;

    private GdefTable(int glyphClasses, int ligatureCarets, int markAttachmentClasses, int markGlyphSets, int variationStore)
    {
        _glyphClasses = glyphClasses;
        _ligatureCarets = ligatureCarets;
        _markAttachmentClasses = markAttachmentClasses;
        _markGlyphSets = markGlyphSets;
        VariationStore = variationStore;
    }

    /// <summary>
    /// Gets the position of the item variation store of the table, which holds the deltas of the positions of the GPOS
    /// table in a variable font; 0 when the table has none.
    /// </summary>
    public int VariationStore { get; }

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
        var ligatureCarets = Validate(table, FontBinary.UInt16(table, 8));
        var markAttachmentClasses = Validate(table, FontBinary.UInt16(table, 10));
        var markGlyphSets = minor >= 2 && table.Length >= 14 ? Validate(table, FontBinary.UInt16(table, 12)) : 0;
        var variationStore = 0;
        if (minor >= 3 && table.Length >= 18)
        {
            // A store that is not readable is left out: the positions then do not vary.
            var offset = FontBinary.UInt32(table, 14);
            variationStore = offset != 0 && offset <= table.Length - 8 && FontBinary.UInt16(table, (int)offset) == 1 ? (int)offset : 0;
        }

        return new GdefTable(glyphClasses, ligatureCarets, markAttachmentClasses, markGlyphSets, variationStore);
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

    /// <summary>
    /// Gets the class of a glyph as the bits of <see cref="GlyphInfo"/> in the low byte, and the attachment class of a
    /// mark in the high byte. The classes are kept by pages of 256 glyphs, built the first time a glyph of the page is
    /// looked up; a page is complete when it is published.
    /// </summary>
    public int GetGlyphClasses(ReadOnlySpan<byte> table, int glyph)
    {
        if ((uint)glyph > 0xFFFF)
            return GetGlyphClass(table, glyph) | (GetMarkAttachmentClass(table, glyph) << 8);

        var pages = Volatile.Read(ref _classPages);
        if (pages is null)
        {
            pages = new ushort[]?[256];
            pages = Interlocked.CompareExchange(ref _classPages, pages, null) ?? pages;
        }

        var page = Volatile.Read(ref pages[glyph >> 8]);
        if (page is null)
        {
            page = new ushort[256];
            var first = glyph & ~0xFF;
            try
            {
                for (var i = 0; i < page.Length; i++)
                {
                    page[i] = (ushort)(GetGlyphClass(table, first + i) | (GetMarkAttachmentClass(table, first + i) << 8));
                }
            }
            catch (InvalidFontException)
            {
                // A malformed class definition fails for the glyphs that read past its end: the other glyphs of the
                // page still have their classes, read one at a time.
                return GetGlyphClass(table, glyph) | (GetMarkAttachmentClass(table, glyph) << 8);
            }

            Volatile.Write(ref pages[glyph >> 8], page);
        }

        return page[glyph & 0xFF];
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

    /// <summary>
    /// Gets the positions of the carets inside a ligature glyph, in font units along its advance: OpenType specification,
    /// "GDEF", "Ligature Caret List Table". A caret value of format 3 varies with the instance of a variable font.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="face">The font, for the deltas of its instance.</param>
    /// <param name="glyph">The ligature glyph.</param>
    /// <param name="carets">Receives the positions, in the order of the components of the ligature.</param>
    /// <returns>
    /// The number of carets the table gives for the glyph (at most the length of <paramref name="carets"/>); 0 when the
    /// table gives none, or gives one by a point of the outline (format 2), which is not read.
    /// </returns>
    /// <exception cref="InvalidFontException">The list is malformed.</exception>
    public int GetLigatureCarets(ReadOnlySpan<byte> table, FontFace face, int glyph, Span<int> carets)
    {
        if (_ligatureCarets == 0)
            return 0;

        var index = LayoutCommon.GetCoverage(table, _ligatureCarets + FontBinary.UInt16(table, _ligatureCarets), glyph);
        if (index == LayoutCommon.NotCovered || index >= FontBinary.UInt16(table, _ligatureCarets + 2))
            return 0;

        var ligature = _ligatureCarets + FontBinary.UInt16(table, _ligatureCarets + 4 + (index * 2));
        var count = Math.Min(FontBinary.UInt16(table, ligature), carets.Length);
        for (var i = 0; i < count; i++)
        {
            var caret = ligature + FontBinary.UInt16(table, ligature + 2 + (i * 2));
            switch (FontBinary.UInt16(table, caret))
            {
                case 1:
                    carets[i] = FontBinary.Int16(table, caret + 2);
                    break;

                case 3:
                    {
                        var value = FontBinary.Int16(table, caret + 2);
                        var device = FontBinary.UInt16(table, caret + 4);
                        if (device != 0 && face.Instance is not null && FontBinary.UInt16(table, caret + device + 4) == 0x8000)
                        {
                            value += face.GetLayoutDelta(FontBinary.UInt16(table, caret + device), FontBinary.UInt16(table, caret + device + 2));
                        }

                        carets[i] = value;
                        break;
                    }

                default:
                    return 0;
            }
        }

        return count;
    }

    private static int Validate(ReadOnlySpan<byte> table, int offset) => offset != 0 && offset <= table.Length - 4 ? offset : 0;
}
