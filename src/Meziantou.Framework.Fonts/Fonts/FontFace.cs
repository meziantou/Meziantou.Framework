namespace Meziantou.Framework.Fonts;

/// <summary>One face of a font file: its table directory and what its fixed-size tables declare.</summary>
/// <remarks>
/// <para>
/// A face is immutable and holds no cache: it can be used by several threads at the same time, each with its own
/// <see cref="GlyphScratch"/>.
/// </para>
/// <para>
/// What every use of the font needs is validated when the face is created: the table directory, the required tables,
/// the headers, the sizes of the metrics and location tables, the structure of the character map. The data of a glyph
/// is validated when the glyph is decoded.
/// </para>
/// </remarks>
public sealed class FontFace
{
    private readonly SfntDirectory _directory;
    private readonly CmapTable _cmap;
    private readonly int _horizontalMetricCount;
    private readonly bool _hasLongLocations;
    private readonly int _hmtxOffset;
    private readonly int _hmtxLength;
    private readonly int _locaOffset;
    private readonly int _locaLength;
    private readonly int _glyfOffset;
    private readonly int _glyfLength;
    private readonly KernTable? _kern;
    private readonly int _kernOffset;
    private readonly int _kernLength;
    private readonly int _gdefOffset;
    private readonly int _gdefLength;
    private readonly int _gsubOffset;
    private readonly int _gsubLength;
    private readonly int _gposOffset;
    private readonly int _gposLength;
    private readonly MathTable? _math;
    private readonly int _mathOffset;
    private readonly int _mathLength;
    private readonly CffTable? _cff;
    private readonly int _cffOffset;
    private readonly int _cffLength;
    private readonly FvarTable? _fvar;
    private readonly int _avarOffset;
    private readonly int _avarLength;
    private readonly GvarTable? _gvar;
    private readonly int _gvarOffset;
    private readonly int _gvarLength;
    private readonly int _hvarOffset;
    private readonly int _hvarLength;
    private readonly int _hvarStore;

    // The advance widths of an instance with an HVAR table, by pages of 256 glyphs (see GetVariedAdvanceWidth).
    private int[]?[]? _advancePages;
    private readonly int _hvarAdvanceMap;
    private readonly int _mvarOffset;
    private readonly int _mvarLength;

    // The vertical metrics: the advances and the top side bearings, the vertical origins, and their variations
    private readonly int _verticalMetricCount;
    private readonly int _vmtxOffset;
    private readonly int _vmtxLength;
    private readonly int _vorgOffset;
    private readonly int _vorgLength;
    private readonly int _vvarOffset;
    private readonly int _vvarLength;
    private readonly int _vvarStore;
    private readonly int _vvarAdvanceMap;
    private readonly int _vvarOriginMap;

    // The baselines of each script
    private readonly BaseTable _base;
    private readonly int _baseOffset;
    private readonly int _baseLength;

    private readonly ColrTable? _colr;
    private readonly int _colrOffset;
    private readonly int _colrLength;
    private readonly CpalTable? _cpal;
    private readonly int _cpalOffset;
    private readonly int _cpalLength;
    private readonly SbixTable? _sbix;
    private readonly int _sbixOffset;
    private readonly int _sbixLength;
    private readonly CblcTable? _eblc;
    private readonly int _eblcOffset;
    private readonly int _eblcLength;
    private readonly int _ebdtOffset;
    private readonly int _ebdtLength;
    private readonly CblcTable? _cblc;
    private readonly int _cblcOffset;
    private readonly int _cblcLength;
    private readonly int _cbdtOffset;
    private readonly int _cbdtLength;

    // The face of the default instance, which the other instances are made from: itself for the face that was loaded.
    private readonly FontFace _default;
    private FontInstance? _instance;

    internal FontFace(FontData data, long directoryOffset)
    {
        _default = this;
        Data = data;
        var file = data.RawBytes.AsSpan();
        _directory = SfntDirectory.Read(file, directoryOffset);

        // head: OpenType specification, "head - Font Header Table".
        var head = GetRequiredTable(FontTags.Head, 54);
        if (FontBinary.UInt32(head, 12) != 0x5F0F3CF5)
            throw FontErrors.InvalidTable(FontTags.Head, "does not have its magic number.");

        UnitsPerEm = FontBinary.UInt16(head, 18);
        if (UnitsPerEm is < 16 or > 16384)
            throw FontErrors.InvalidTable(FontTags.Head, $"declares {UnitsPerEm} units per em; the valid range is 16 to 16384.");

        HeadFlags = FontBinary.UInt16(head, 16);
        XMin = FontBinary.Int16(head, 36);
        YMin = FontBinary.Int16(head, 38);
        XMax = FontBinary.Int16(head, 40);
        YMax = FontBinary.Int16(head, 42);
        var macStyle = FontBinary.UInt16(head, 44);
        MacStyle = macStyle;
        var locationFormat = FontBinary.Int16(head, 50);
        if (locationFormat is not (0 or 1))
            throw FontErrors.InvalidTable(FontTags.Head, $"declares the unknown location format {locationFormat}.");

        _hasLongLocations = locationFormat == 1;

        // maxp: the number of glyphs.
        var maxp = GetRequiredTable(FontTags.Maxp, 6);
        GlyphCount = FontBinary.UInt16(maxp, 4);
        if (GlyphCount == 0)
            throw FontErrors.InvalidTable(FontTags.Maxp, "declares no glyph.");

        // hhea and hmtx: the horizontal metrics.
        var hhea = GetRequiredTable(FontTags.Hhea, 36);
        HorizontalAscender = FontBinary.Int16(hhea, 4);
        HorizontalDescender = FontBinary.Int16(hhea, 6);
        HorizontalLineGap = FontBinary.Int16(hhea, 8);
        _horizontalMetricCount = FontBinary.UInt16(hhea, 34);
        if (_horizontalMetricCount == 0 || _horizontalMetricCount > GlyphCount)
            throw FontErrors.InvalidTable(FontTags.Hhea, $"declares {_horizontalMetricCount} horizontal metrics for {GlyphCount} glyphs.");

        if (!_directory.TryFind(FontTags.Hmtx, out _hmtxOffset, out _hmtxLength) || _hmtxLength < (long)_horizontalMetricCount * 4)
            throw FontErrors.InvalidTable(FontTags.Hmtx, "is missing or shorter than the number of horizontal metrics.");

        // vhea, vmtx and VORG: the vertical metrics, which a font made for horizontal text only does not have. Tables
        // that do not agree with each other are left out: the glyphs then have the vertical metrics of such a font.
        if (TryGetTable(FontTags.Vhea, out var vheaMemory) && vheaMemory.Length >= 36
            && _directory.TryFind(FontTags.Vmtx, out var vmtxOffset, out var vmtxLength))
        {
            var vhea = vheaMemory.Span;
            var count = FontBinary.UInt16(vhea, 34);
            if (count != 0 && count <= GlyphCount && vmtxLength >= (long)count * 4)
            {
                _verticalMetricCount = count;
                _vmtxOffset = vmtxOffset;
                _vmtxLength = vmtxLength;
                VerticalAscender = FontBinary.Int16(vhea, 4);
                VerticalDescender = FontBinary.Int16(vhea, 6);
                VerticalLineGap = FontBinary.Int16(vhea, 8);
            }
        }

        if (_directory.TryFind(FontTags.Vorg, out var vorgOffset, out var vorgLength) && vorgLength >= 8
            && FontBinary.UInt16(file.Slice(vorgOffset, vorgLength), 0) == 1
            && vorgLength >= 8 + ((long)FontBinary.UInt16(file.Slice(vorgOffset, vorgLength), 6) * 4))
        {
            _vorgOffset = vorgOffset;
            _vorgLength = vorgLength;
        }

        // BASE: the baselines. A table whose header is not understood is left out: the font then has no baseline
        // table, and text is aligned with its ascent and its descent.
        if (_directory.TryFind(FontTags.Base, out var baseOffset, out var baseLength) && BaseTable.TryRead(file.Slice(baseOffset, baseLength), out _base))
        {
            _baseOffset = baseOffset;
            _baseLength = baseLength;
        }

        // OS/2 (optional in old fonts), post and name: the names and the style are read as the system font index reads
        // them, by FaceStyle. A field of OS/2 beyond the table is absent.
        TryGetTable(FontTags.Os2, out var os2Memory);
        TryGetTable(FontTags.Post, out var postMemory);
        TryGetTable(FontTags.Name, out var nameMemory);
        var style = FaceStyle.Read(macStyle, os2Memory.Span, postMemory.Span, nameMemory.Span);
        HasOs2 = style.HasOs2;
        Weight = style.Weight;
        WidthClass = style.WidthClass;
        Selection = style.Selection;
        IsItalic = style.IsItalic;
        HasPost = style.HasPost;
        ItalicAngle = style.ItalicAngle;
        IsMonospaced = style.IsMonospaced;
        FamilyName = style.FamilyName;
        SubfamilyName = style.SubfamilyName;
        PostScriptName = style.PostScriptName;
        if (style.HasOs2)
        {
            var os2 = os2Memory.Span;
            Os2Version = FontBinary.UInt16(os2, 0);
            AverageCharWidth = FontBinary.Int16(os2, 2);
            SubscriptYOffset = FontBinary.Int16(os2, 16);
            SuperscriptYOffset = FontBinary.Int16(os2, 24);
            StrikeoutSize = FontBinary.Int16(os2, 26);
            StrikeoutPosition = FontBinary.Int16(os2, 28);
            UseTypoMetrics = (style.Selection & 0x0080) != 0;
            TypoAscender = FontBinary.Int16(os2, 68);
            TypoDescender = FontBinary.Int16(os2, 70);
            TypoLineGap = FontBinary.Int16(os2, 72);
            WinAscent = FontBinary.UInt16(os2, 74);
            WinDescent = FontBinary.UInt16(os2, 76);
            if (Os2Version >= 2 && os2.Length >= 90)
            {
                XHeight = FontBinary.Int16(os2, 86);
                CapHeight = FontBinary.Int16(os2, 88);
            }
        }

        // post: the underline.
        if (style.HasPost)
        {
            var post = postMemory.Span;
            UnderlinePosition = FontBinary.Int16(post, 8);
            UnderlineThickness = FontBinary.Int16(post, 10);
        }

        if (!TryGetTable(FontTags.Cmap, out var cmap) || cmap.Length < 4)
            throw FontErrors.InvalidTable(FontTags.Cmap, "is missing.");

        _cmap = CmapTable.Read(cmap, GlyphCount);

        if (_directory.TryFind(FontTags.Kern, out _kernOffset, out _kernLength))
        {
            var kern = KernTable.Read(file.Slice(_kernOffset, _kernLength));
            _kern = kern.IsEmpty ? null : kern;
        }

        // MATH: the constants and the glyph variants of mathematical layout. A table whose header is not understood is
        // left out: the font then has no mathematical data.
        if (_directory.TryFind(FontTags.Math, out _mathOffset, out _mathLength))
        {
            _math = MathTable.Read(file.Slice(_mathOffset, _mathLength));
        }

        // The layout tables. A table whose header is not understood is left out: the font is used without it.
        if (_directory.TryFind(FontTags.Gdef, out _gdefOffset, out _gdefLength))
        {
            Gdef = GdefTable.Read(file.Slice(_gdefOffset, _gdefLength));
        }

        if (_directory.TryFind(FontTags.Gsub, out _gsubOffset, out _gsubLength))
        {
            Gsub = LayoutTable.Read(file.Slice(_gsubOffset, _gsubLength));
        }

        if (_directory.TryFind(FontTags.Gpos, out _gposOffset, out _gposLength))
        {
            Gpos = LayoutTable.Read(file.Slice(_gposOffset, _gposLength));
        }

        // The outlines: TrueType (glyf and loca), or CFF.
        if (_directory.TryFind(FontTags.Glyf, out _glyfOffset, out _glyfLength))
        {
            var entrySize = _hasLongLocations ? 4 : 2;
            if (!_directory.TryFind(FontTags.Loca, out _locaOffset, out _locaLength) || _locaLength < ((long)GlyphCount + 1) * entrySize)
                throw FontErrors.InvalidTable(FontTags.Loca, "is missing or shorter than the number of glyphs.");

            Outlines = FontOutlineKind.TrueType;
        }
        else if (_directory.TryFind(FontTags.Cff, out _cffOffset, out _cffLength))
        {
            _cff = CffTable.Read(file.Slice(_cffOffset, _cffLength));
            Outlines = FontOutlineKind.Cff;
        }
        else if (_directory.TryFind(FontTags.Cff2, out _cffOffset, out _cffLength))
        {
            _cff = CffTable.Read(file.Slice(_cffOffset, _cffLength), isCff2: true);
            Outlines = FontOutlineKind.Cff2;
        }
        else
        {
            Outlines = FontOutlineKind.None;
        }

        // The color glyphs: the palettes, and the layers or the paints of the glyphs. A table whose header is not
        // understood is left out: the glyphs are then drawn with their outlines.
        if (_directory.TryFind(FontTags.Cpal, out _cpalOffset, out _cpalLength))
        {
            _cpal = CpalTable.Read(file.Slice(_cpalOffset, _cpalLength));
        }

        if (_directory.TryFind(FontTags.Colr, out _colrOffset, out _colrLength))
        {
            _colr = ColrTable.Read(file.Slice(_colrOffset, _colrLength));
        }

        // The glyphs that are images: the strikes of the sbix table, or the color bitmaps of the CBLC and CBDT tables.
        if (_directory.TryFind(FontTags.Sbix, out _sbixOffset, out _sbixLength))
        {
            _sbix = SbixTable.Read(file.Slice(_sbixOffset, _sbixLength), GlyphCount);
        }

        if (_directory.TryFind(FontTags.Cblc, out _cblcOffset, out _cblcLength) && _directory.TryFind(FontTags.Cbdt, out _cbdtOffset, out _cbdtLength))
        {
            _cblc = CblcTable.Read(file.Slice(_cblcOffset, _cblcLength));
        }

        // The bitmaps without colors, for the font that was loaded to use them.
        if (data.UseEmbeddedBitmaps && _directory.TryFind(FontTags.Eblc, out _eblcOffset, out _eblcLength) && _directory.TryFind(FontTags.Ebdt, out _ebdtOffset, out _ebdtLength))
        {
            _eblc = CblcTable.Read(file.Slice(_eblcOffset, _eblcLength));
        }

        // The variations: the axes, then the tables that vary the outlines, the advances and the metrics along them.
        // A table whose header is not understood is left out: what it varies keeps its default value.
        if (_directory.TryFind(FontTags.Fvar, out var fvarOffset, out var fvarLength))
        {
            _fvar = FvarTable.Read(file.Slice(fvarOffset, fvarLength));
        }

        if (_fvar is not null)
        {
            _directory.TryFind(FontTags.Avar, out _avarOffset, out _avarLength);
            _directory.TryFind(FontTags.Mvar, out _mvarOffset, out _mvarLength);
            if (Outlines == FontOutlineKind.TrueType && _directory.TryFind(FontTags.Gvar, out _gvarOffset, out _gvarLength))
            {
                _gvar = GvarTable.Read(file.Slice(_gvarOffset, _gvarLength), _fvar.Axes.Length);
            }

            // HVAR: OpenType specification, "HVAR - Horizontal Metrics Variations Table".
            if (_directory.TryFind(FontTags.Hvar, out _hvarOffset, out _hvarLength) && _hvarLength >= 20)
            {
                var hvar = file.Slice(_hvarOffset, _hvarLength);
                var store = FontBinary.UInt32(hvar, 4);
                var map = FontBinary.UInt32(hvar, 8);
                if (FontBinary.UInt16(hvar, 0) == 1 && store != 0 && store <= hvar.Length - 8 && map <= hvar.Length - 4 && FontBinary.UInt16(hvar, (int)store) == 1)
                {
                    _hvarStore = (int)store;
                    _hvarAdvanceMap = (int)map;
                }
            }

            // VVAR: OpenType specification, "VVAR - Vertical Metrics Variations Table". The same form as HVAR, with a
            // mapping for the vertical origins after the three mappings of the metrics.
            if (_directory.TryFind(FontTags.Vvar, out _vvarOffset, out _vvarLength) && _vvarLength >= 24)
            {
                var vvar = file.Slice(_vvarOffset, _vvarLength);
                var store = FontBinary.UInt32(vvar, 4);
                var map = FontBinary.UInt32(vvar, 8);
                var originMap = FontBinary.UInt32(vvar, 20);
                if (FontBinary.UInt16(vvar, 0) == 1 && store != 0 && store <= vvar.Length - 8 && map <= vvar.Length - 4 && originMap <= vvar.Length - 4 && FontBinary.UInt16(vvar, (int)store) == 1)
                {
                    _vvarStore = (int)store;
                    _vvarAdvanceMap = (int)map;
                    _vvarOriginMap = (int)originMap;
                }
            }
        }
    }

    /// <summary>Gets the bytes of the font file the face comes from.</summary>
    public FontData Data { get; }

    /// <summary>Gets the number of font units in the em square (<c>head.unitsPerEm</c>).</summary>
    public int UnitsPerEm { get; }

    /// <summary>Gets the number of glyphs of the face (<c>maxp.numGlyphs</c>).</summary>
    public int GlyphCount { get; }

    /// <summary>Gets the kind of outlines of the face.</summary>
    public FontOutlineKind Outlines { get; }

    /// <summary>Gets the family name of the face, from the <c>name</c> table.</summary>
    public string FamilyName { get; }

    /// <summary>Gets the subfamily name of the face, such as <c>Bold Italic</c>, from the <c>name</c> table.</summary>
    public string SubfamilyName { get; }

    /// <summary>Gets the PostScript name of the face, or <see langword="null"/> when the font has none.</summary>
    public string? PostScriptName { get; }

    /// <summary>Gets the weight of the face, from 1 to 1000 (400 is regular, 700 is bold).</summary>
    public int Weight { get; private set; }

    /// <summary>Gets a value indicating whether the face is italic or oblique.</summary>
    public bool IsItalic { get; }

    /// <summary>Gets a value indicating whether all the glyphs of the face have the same advance width.</summary>
    public bool IsMonospaced { get; }

    /// <summary>Gets <c>hhea.ascender</c>, in font units.</summary>
    public int HorizontalAscender { get; private set; }

    /// <summary>Gets <c>hhea.descender</c>, in font units, negative below the baseline.</summary>
    public int HorizontalDescender { get; private set; }

    /// <summary>Gets <c>hhea.lineGap</c>, in font units.</summary>
    public int HorizontalLineGap { get; private set; }

    /// <summary>Gets <c>head.flags</c>.</summary>
    public int HeadFlags { get; }

    /// <summary>Gets <c>head.macStyle</c>: bit 0 is bold, bit 1 is italic.</summary>
    public int MacStyle { get; }

    /// <summary>Gets the left of the box of all the glyphs of the font (<c>head.xMin</c>), in font units.</summary>
    public int XMin { get; }

    /// <summary>Gets the bottom of the box of all the glyphs of the font (<c>head.yMin</c>), in font units.</summary>
    public int YMin { get; }

    /// <summary>Gets the right of the box of all the glyphs of the font (<c>head.xMax</c>), in font units.</summary>
    public int XMax { get; }

    /// <summary>Gets the top of the box of all the glyphs of the font (<c>head.yMax</c>), in font units.</summary>
    public int YMax { get; }

    /// <summary>Gets a value indicating whether the face has an OS/2 table.</summary>
    public bool HasOs2 { get; }

    /// <summary>Gets the version of the OS/2 table; 0 when the font has none.</summary>
    public int Os2Version { get; }

    /// <summary>Gets <c>OS/2.xAvgCharWidth</c>, in font units; 0 when the font has no OS/2 table.</summary>
    public int AverageCharWidth { get; }

    /// <summary>Gets <c>OS/2.usWidthClass</c>, from 1 (ultra-condensed) to 9 (ultra-expanded); 5 when the font has no OS/2 table.</summary>
    public int WidthClass { get; } = 5;

    /// <summary>Gets <c>OS/2.fsSelection</c>; 0 when the font has no OS/2 table.</summary>
    public int Selection { get; }

    /// <summary>Gets <c>OS/2.ySubscriptYOffset</c>: how far below the baseline subscripts are, in font units, positive downward.</summary>
    public int SubscriptYOffset { get; private set; }

    /// <summary>Gets <c>OS/2.ySuperscriptYOffset</c>: how far above the baseline superscripts are, in font units.</summary>
    public int SuperscriptYOffset { get; private set; }

    /// <summary>Gets a value indicating whether the font asks that the typographic metrics of the OS/2 table are used for the line spacing (<c>USE_TYPO_METRICS</c>).</summary>
    public bool UseTypoMetrics { get; }

    /// <summary>Gets <c>OS/2.sTypoAscender</c>, in font units.</summary>
    public int TypoAscender { get; private set; }

    /// <summary>Gets <c>OS/2.sTypoDescender</c>, in font units, negative below the baseline.</summary>
    public int TypoDescender { get; private set; }

    /// <summary>Gets <c>OS/2.sTypoLineGap</c>, in font units.</summary>
    public int TypoLineGap { get; private set; }

    /// <summary>Gets <c>OS/2.usWinAscent</c>, in font units.</summary>
    public int WinAscent { get; private set; }

    /// <summary>Gets <c>OS/2.usWinDescent</c>, in font units, positive below the baseline.</summary>
    public int WinDescent { get; private set; }

    /// <summary>Gets <c>OS/2.sxHeight</c>: the height of the lowercase letters, in font units.</summary>
    public int XHeight { get; private set; }

    /// <summary>Gets <c>OS/2.sCapHeight</c>: the height of the uppercase letters, in font units.</summary>
    public int CapHeight { get; private set; }

    /// <summary>Gets <c>OS/2.yStrikeoutPosition</c>: the top of the strikeout above the baseline, in font units.</summary>
    public int StrikeoutPosition { get; private set; }

    /// <summary>Gets <c>OS/2.yStrikeoutSize</c>: the thickness of the strikeout, in font units.</summary>
    public int StrikeoutSize { get; private set; }

    /// <summary>Gets a value indicating whether the face has a <c>post</c> table.</summary>
    public bool HasPost { get; }

    /// <summary>Gets <c>post.italicAngle</c>, in degrees counterclockwise from the vertical; 0 for an upright font.</summary>
    public double ItalicAngle { get; }

    /// <summary>Gets <c>post.underlinePosition</c>: the top of the underline, in font units, negative below the baseline.</summary>
    public int UnderlinePosition { get; private set; }

    /// <summary>Gets <c>post.underlineThickness</c>: the thickness of the underline, in font units.</summary>
    public int UnderlineThickness { get; private set; }

    /// <summary>Gets the glyph definition table, or <see langword="null"/>.</summary>
    internal GdefTable? Gdef { get; }

    /// <summary>Gets the bytes of the <c>GDEF</c> table; empty when the face has none.</summary>
    public ReadOnlyMemory<byte> GdefData => Gdef is null ? default : Data.RawBytes.AsMemory(_gdefOffset, _gdefLength);

    /// <summary>Gets the glyph substitution table, or <see langword="null"/>.</summary>
    public LayoutTable? Gsub { get; }

    /// <summary>Gets the bytes of the <c>GSUB</c> table; empty when the face has none.</summary>
    public ReadOnlyMemory<byte> GsubData => Gsub is null ? default : Data.RawBytes.AsMemory(_gsubOffset, _gsubLength);

    /// <summary>Gets the glyph positioning table, or <see langword="null"/>.</summary>
    internal LayoutTable? Gpos { get; }

    /// <summary>Gets the bytes of the <c>GPOS</c> table; empty when the face has none.</summary>
    public ReadOnlyMemory<byte> GposData => Gpos is null ? default : Data.RawBytes.AsMemory(_gposOffset, _gposLength);

    /// <summary>Gets the tags of the tables of the face, in increasing order.</summary>
    public ReadOnlySpan<uint> TableTags => _directory.Tags;

    /// <summary>Gets a table of the face.</summary>
    /// <returns><see langword="false"/> when the face does not have the table.</returns>
    public bool TryGetTable(uint tag, out ReadOnlyMemory<byte> table)
    {
        if (_directory.TryFind(tag, out var offset, out var length))
        {
            table = Data.RawBytes.AsMemory(offset, length);
            return true;
        }

        table = default;
        return false;
    }

    /// <summary>Gets the glyph of a character, or 0 when the font has none.</summary>
    public int MapCodePoint(int codePoint) => _cmap.Map(codePoint);

    /// <summary>Gets the glyph of a variation sequence.</summary>
    public CmapVariation MapVariation(int codePoint, int selector, out int glyphId) => _cmap.MapVariation(codePoint, selector, out glyphId);

    /// <summary>
    /// Gets a value indicating whether the advances of the glyphs come from their outlines: an instance of a variable font
    /// with TrueType outlines and without <c>HVAR</c> table, whose advance is the one of the phantom points of a glyph.
    /// </summary>
    public bool VariesAdvancesWithOutlines => _instance is not null && _hvarStore == 0 && _gvar is not null;

    /// <summary>Gets the advance width of a glyph, in font units.</summary>
    /// <remarks>
    /// In an instance of a variable font, the advance of the default instance varies by the delta of the <c>HVAR</c>
    /// table, rounded to a font unit; a font with TrueType outlines and without this table varies its advances with
    /// the phantom points of its glyphs.
    /// </remarks>
    public int GetAdvanceWidth(int glyphId)
    {
        if ((uint)glyphId >= (uint)GlyphCount)
            return 0;

        // The glyphs after the last metric share its advance.
        var index = Math.Min(glyphId, _horizontalMetricCount - 1);
        var advance = FontBinary.UInt16(Data.RawBytes.AsSpan(_hmtxOffset, _hmtxLength), index * 4);
        if (_instance is null)
            return advance;

        if (_hvarStore != 0)
            return GetVariedAdvanceWidth(glyphId, advance);

        if (_gvar is not null)
        {
            var budget = new GlyphBudget(Data.Limits);
            return ItemVariationStore.Round(GlyfOutlineReader.GetAdvance(CreateGlyfContext(), glyphId, ref budget));
        }

        return advance;
    }

    /// <summary>
    /// Gets the advance width of a glyph of an instance with the delta of the <c>HVAR</c> table. The advances are kept by
    /// pages of 256 glyphs, computed the first time a glyph of the page is looked up; a page is complete when it is
    /// published.
    /// </summary>
    private int GetVariedAdvanceWidth(int glyphId, int advance)
    {
        var pages = Volatile.Read(ref _advancePages);
        if (pages is null)
        {
            pages = new int[]?[(GlyphCount + 255) >> 8];
            pages = Interlocked.CompareExchange(ref _advancePages, pages, null) ?? pages;
        }

        var page = Volatile.Read(ref pages[glyphId >> 8]);
        if (page is null)
        {
            page = new int[256];
            var first = glyphId & ~0xFF;
            try
            {
                for (var i = 0; i < page.Length && first + i < GlyphCount; i++)
                {
                    var index = Math.Min(first + i, _horizontalMetricCount - 1);
                    page[i] = ComputeVariedAdvanceWidth(first + i, FontBinary.UInt16(Data.RawBytes.AsSpan(_hmtxOffset, _hmtxLength), index * 4));
                }
            }
            catch (InvalidFontException)
            {
                // A malformed table fails for the glyphs whose deltas read past its end, as it does when they are read.
                return ComputeVariedAdvanceWidth(glyphId, advance);
            }

            Volatile.Write(ref pages[glyphId >> 8], page);
        }

        return page[glyphId & 0xFF];
    }

    private int ComputeVariedAdvanceWidth(int glyphId, int advance)
    {
        var hvar = Data.RawBytes.AsSpan(_hvarOffset, _hvarLength);
        var outer = 0;
        var inner = glyphId;
        if (_hvarAdvanceMap != 0)
        {
            ItemVariationStore.ReadIndexMap(hvar, _hvarAdvanceMap, glyphId, out outer, out inner);
        }

        return advance + ItemVariationStore.Round(ItemVariationStore.GetDelta(hvar, _hvarStore, outer, inner, _instance!.AdvanceScalars));
    }

    /// <summary>Gets a value indicating whether the font has vertical metrics of its own (<c>vhea</c> and <c>vmtx</c>).</summary>
    public bool HasVerticalMetrics => _verticalMetricCount != 0;

    /// <summary>Gets <c>vhea.vertTypoAscender</c>, in font units; 0 when the font has no vertical metrics.</summary>
    public int VerticalAscender { get; private set; }

    /// <summary>Gets <c>vhea.vertTypoDescender</c>, in font units; 0 when the font has no vertical metrics.</summary>
    public int VerticalDescender { get; private set; }

    /// <summary>Gets <c>vhea.vertTypoLineGap</c>, in font units; 0 when the font has no vertical metrics.</summary>
    public int VerticalLineGap { get; private set; }

    /// <summary>Gets the left side bearing of a glyph in the <c>hmtx</c> table, in font units: the one of the default instance of a variable font.</summary>
    public int GetLeftSideBearing(int glyphId)
    {
        if ((uint)glyphId >= (uint)GlyphCount)
            return 0;

        // The glyphs after the last metric have their bearing alone, after the metrics. A table that ends before them
        // (which old fonts do) gives them 0.
        var hmtx = Data.RawBytes.AsSpan(_hmtxOffset, _hmtxLength);
        var offset = glyphId < _horizontalMetricCount ? (glyphId * 4) + 2 : (_horizontalMetricCount * 4) + ((glyphId - _horizontalMetricCount) * 2);
        return offset + 2 <= hmtx.Length ? FontBinary.Int16(hmtx, offset) : 0;
    }

    /// <summary>
    /// Gets the top side bearing of a glyph in the <c>vmtx</c> table, in font units: the one of the default instance of
    /// a variable font; 0 when the font has no vertical metrics.
    /// </summary>
    public int GetTopSideBearing(int glyphId)
    {
        if ((uint)glyphId >= (uint)GlyphCount || _verticalMetricCount == 0)
            return 0;

        var vmtx = Data.RawBytes.AsSpan(_vmtxOffset, _vmtxLength);
        var offset = glyphId < _verticalMetricCount ? (glyphId * 4) + 2 : (_verticalMetricCount * 4) + ((glyphId - _verticalMetricCount) * 2);
        return offset + 2 <= vmtx.Length ? FontBinary.Int16(vmtx, offset) : 0;
    }

    /// <summary>
    /// Gets the box of the outline of a glyph, in font units: the box the <c>glyf</c> table declares for the glyph in a
    /// font with TrueType outlines that does not vary them, otherwise the box of the points of its outline, rounded
    /// outward.
    /// </summary>
    /// <param name="glyphId">The glyph.</param>
    /// <param name="scratch">The working storage of the outline, when it has to be decoded.</param>
    /// <param name="bounds">The box.</param>
    /// <returns><see langword="false"/> for a glyph without outline, such as a space.</returns>
    /// <exception cref="InvalidFontException">The data of the glyph is malformed.</exception>
    /// <exception cref="UnsupportedFontFeatureException">The outlines of the font are in a format that is not supported.</exception>
    /// <exception cref="FontResourceLimitException">The glyph exceeds the complexity limit or the allocation limit.</exception>
    public bool TryGetGlyphBounds(int glyphId, GlyphScratch scratch, out GlyphBox bounds)
    {
        bounds = default;
        if ((uint)glyphId >= (uint)GlyphCount)
            return false;

        if (Outlines == FontOutlineKind.TrueType && (_instance is null || _gvar is null))
        {
            // The box the font declares for the glyph, at the start of its description
            var bytes = Data.RawBytes.AsSpan();
            var loca = bytes.Slice(_locaOffset, _locaLength);
            long start = _hasLongLocations ? FontBinary.UInt32(loca, glyphId * 4) : FontBinary.UInt16(loca, glyphId * 2) * 2;
            long end = _hasLongLocations ? FontBinary.UInt32(loca, (glyphId + 1) * 4) : FontBinary.UInt16(loca, (glyphId + 1) * 2) * 2;
            if (end - start < 10 || end > _glyfLength)
                return false;

            var glyph = bytes.Slice(_glyfOffset + (int)start, 10);
            bounds = new GlyphBox(FontBinary.Int16(glyph, 2), FontBinary.Int16(glyph, 4), FontBinary.Int16(glyph, 6), FontBinary.Int16(glyph, 8));
            return true;
        }

        if (Outlines == FontOutlineKind.None)
            return false;

        // The box of the points of the outline, which is the box of an outline that has a point at each extremum
        var sink = new GlyphBoundsSink();
        var budget = new GlyphBudget(Data.Limits);
        DecodeOutline(glyphId, sink, scratch, ref budget);
        if (sink.IsEmpty)
            return false;

        bounds = new GlyphBox((int)Math.Floor(sink.MinX), (int)Math.Floor(sink.MinY), (int)Math.Ceiling(sink.MaxX), (int)Math.Ceiling(sink.MaxY));
        return true;
    }

    /// <summary>
    /// Gets the ascender and the descender a line of horizontal text has, as two heights from the baseline: those of
    /// the OS/2 table when it asks for them, those of the hhea table otherwise.
    /// </summary>
    private void GetLineExtents(out int ascender, out int descender)
    {
        var useTypo = HasOs2 && UseTypoMetrics;
        ascender = Math.Abs(useTypo ? TypoAscender : HorizontalAscender);
        descender = Math.Abs(useTypo ? TypoDescender : HorizontalDescender);
    }

    /// <summary>Gets the advance height of a glyph, in font units: how far the pen moves down after it in a vertical line.</summary>
    /// <remarks>
    /// The advance is the one of the <c>vmtx</c> table, varied by the delta of the <c>VVAR</c> table in an instance
    /// of a variable font. A font without vertical metrics gives every glyph the height of a line of its horizontal
    /// text, from its ascender to its descender.
    /// </remarks>
    public int GetAdvanceHeight(int glyphId)
    {
        if ((uint)glyphId >= (uint)GlyphCount)
            return 0;

        if (_verticalMetricCount == 0)
        {
            GetLineExtents(out var ascender, out var descender);
            return ascender + descender;
        }

        // The glyphs after the last metric share its advance.
        var index = Math.Min(glyphId, _verticalMetricCount - 1);
        var advance = FontBinary.UInt16(Data.RawBytes.AsSpan(_vmtxOffset, _vmtxLength), index * 4);
        if (_instance is null || _vvarStore == 0)
            return advance;

        var vvar = Data.RawBytes.AsSpan(_vvarOffset, _vvarLength);
        var outer = 0;
        var inner = glyphId;
        if (_vvarAdvanceMap != 0)
        {
            ItemVariationStore.ReadIndexMap(vvar, _vvarAdvanceMap, glyphId, out outer, out inner);
        }

        return advance + ItemVariationStore.Round(ItemVariationStore.GetDelta(vvar, _vvarStore, outer, inner, _instance.VerticalScalars));
    }

    /// <summary>
    /// Gets the vertical origin of a glyph, in font units from its horizontal origin: the point of the glyph that is
    /// on the pen in a vertical line, at the top of its advance height and in the middle of its width.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Horizontally, the origin is the middle of the advance width. Vertically, it is the one the <c>VORG</c> table
    /// gives (with the delta of the <c>VVAR</c> table in an instance); without that table, the top of the outline
    /// plus the top side bearing of the <c>vmtx</c> table; without vertical metrics, the top of the outline plus half
    /// of what the advance height has more than the outline is tall, which centers the glyph in its advance. A glyph
    /// without outline, such as a space, has an outline without height at the baseline.
    /// </para>
    /// <para>These are the rules of the reference shaper, which the positions of a vertical text are compared with.</para>
    /// </remarks>
    public void GetVerticalOrigin(int glyphId, GlyphScratch? scratch, out int x, out int y)
    {
        x = GetAdvanceWidth(glyphId) / 2;
        var bytes = Data.RawBytes.AsSpan();
        if (_vorgLength != 0)
        {
            var vorg = bytes.Slice(_vorgOffset, _vorgLength);
            y = FontBinary.Int16(vorg, 4);

            // The glyphs that have another origin than the default one, sorted by glyph
            var low = 0;
            var high = FontBinary.UInt16(vorg, 6) - 1;
            while (low <= high)
            {
                var middle = (low + high) >>> 1;
                var glyph = FontBinary.UInt16(vorg, 8 + (middle * 4));
                if (glyph == glyphId)
                {
                    y = FontBinary.Int16(vorg, 10 + (middle * 4));
                    break;
                }

                if (glyph < glyphId)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            if (_instance is not null && _vvarStore != 0 && _vvarOriginMap != 0)
            {
                var vvar = bytes.Slice(_vvarOffset, _vvarLength);
                ItemVariationStore.ReadIndexMap(vvar, _vvarOriginMap, glyphId, out var outer, out var inner);
                y = ItemVariationStore.Round(y + ItemVariationStore.GetDelta(vvar, _vvarStore, outer, inner, _instance.VerticalScalars));
            }

            return;
        }

        // A glyph without outline is measured as an outline without height at the baseline
        GetVerticalExtents(glyphId, scratch, out var top, out var height);
        if (_verticalMetricCount != 0)
        {
            // The top side bearing: with the advance of the glyphs that have one, then alone for the others
            var vmtx = bytes.Slice(_vmtxOffset, _vmtxLength);
            var offset = glyphId < _verticalMetricCount ? (glyphId * 4) + 2 : (_verticalMetricCount * 4) + ((glyphId - _verticalMetricCount) * 2);
            if (offset + 2 <= vmtx.Length)
            {
                y = top + FontBinary.Int16(vmtx, offset);
                return;
            }
        }

        GetLineExtents(out var ascender, out var descender);
        y = top + ((ascender + descender - height) >> 1);
    }

    /// <summary>Gets a baseline of a script, in font units: along the y axis of the glyphs for horizontal lines, along their x axis for vertical lines.</summary>
    /// <param name="isVertical"><see langword="true"/> for the baselines of vertical lines.</param>
    /// <param name="scripts">The script tags of the text, by order of preference.</param>
    /// <param name="baseline">The tag of the baseline.</param>
    /// <param name="coordinate">The coordinate of the baseline, with the delta of the <c>BASE</c> table in an instance of a variable font.</param>
    /// <returns><see langword="false"/> when the font has no <c>BASE</c> table, or the table does not give the baseline.</returns>
    /// <exception cref="InvalidFontException">The table is malformed.</exception>
    public bool TryGetBaseline(bool isVertical, ReadOnlySpan<uint> scripts, uint baseline, out int coordinate)
    {
        var table = Data.RawBytes.AsSpan(_baseOffset, _baseLength);
        if (_baseLength == 0 || !_base.TryGetBaseline(table, isVertical, scripts, baseline, out coordinate, out var outer, out var inner))
        {
            coordinate = 0;
            return false;
        }

        if (outer >= 0 && _instance is not null && _base.VariationStore != 0)
        {
            coordinate = ItemVariationStore.Round(coordinate + ItemVariationStore.GetDelta(table, _base.VariationStore, outer, inner, _instance.BaselineScalars));
        }

        return true;
    }

    /// <summary>
    /// Gets the middle of the ideographic em box of a script, in font units: halfway between the bottom and the top of
    /// the box (the <c>ideo</c> and <c>idtp</c> baselines), one em apart when the table only gives one of them.
    /// </summary>
    /// <param name="isVertical"><see langword="true"/> for the middle of the box of vertical lines, along the x axis of the glyphs.</param>
    /// <param name="scripts">The script tags of the text, by order of preference.</param>
    /// <param name="center">The middle of the box.</param>
    /// <returns><see langword="false"/> when the font does not give the ideographic em box of the script.</returns>
    /// <exception cref="InvalidFontException">The <c>BASE</c> table is malformed.</exception>
    public bool TryGetIdeographicCenter(bool isVertical, ReadOnlySpan<uint> scripts, out double center)
    {
        var hasBottom = TryGetBaseline(isVertical, scripts, BaseTable.IdeographicBottom, out var bottom);
        var hasTop = TryGetBaseline(isVertical, scripts, BaseTable.IdeographicTop, out var top);
        center = (hasBottom, hasTop) switch
        {
            (true, true) => (bottom + top) / 2.0,
            (true, false) => bottom + (UnitsPerEm / 2.0),
            (false, true) => top - (UnitsPerEm / 2.0),
            _ => 0,
        };

        return hasBottom || hasTop;
    }

    /// <summary>Gets the top of the outline of a glyph and its height, in font units: both 0 for a glyph without outline.</summary>
    private void GetVerticalExtents(int glyphId, GlyphScratch? scratch, out int top, out int height)
    {
        top = 0;
        height = 0;
        var isDeclared = Outlines == FontOutlineKind.TrueType && (_instance is null || _gvar is null);
        if ((!isDeclared && scratch is null) || !TryGetGlyphBounds(glyphId, scratch!, out var bounds))
            return;

        top = bounds.YMax;
        height = bounds.YMax - bounds.YMin;
    }

    /// <summary>Gets a value indicating whether the font has glyphs that are drawn with their own colors.</summary>
    public bool HasColorGlyphs => _colr is not null || _sbix is not null || _cblc is not null;

    /// <summary>Finds the image of a glyph the font gives as a bitmap, in the strike that suits a size.</summary>
    /// <returns><see langword="false"/> when the font has no image for the glyph.</returns>
    public bool TryGetBitmapGlyph(int glyphId, double size, out BitmapGlyph glyph)
    {
        var bytes = Data.RawBytes.AsSpan();
        if (_sbix is not null && _sbix.TryGetGlyph(bytes.Slice(_sbixOffset, _sbixLength), _sbixOffset, glyphId, size, out glyph))
            return true;

        if (_cblc is not null && (uint)glyphId < (uint)GlyphCount)
            return _cblc.TryGetGlyph(bytes.Slice(_cblcOffset, _cblcLength), bytes.Slice(_cbdtOffset, _cbdtLength), _cbdtOffset, glyphId, size, out glyph);

        glyph = default;
        return false;
    }

    /// <summary>Gets the number of palettes of colors of the font.</summary>
    public int PaletteCount => _cpal?.PaletteCount ?? 0;

    /// <summary>Gets the number of colors of each palette.</summary>
    public int PaletteEntryCount => _cpal?.EntryCount ?? 0;

    /// <summary>Gets a color of a palette: its red, green, blue and alpha components, from 0 to 255.</summary>
    /// <returns><see langword="false"/> when the font does not have the palette or the entry.</returns>
    public bool TryGetPaletteColor(int palette, int entry, out int red, out int green, out int blue, out int alpha)
    {
        if (_cpal is null)
        {
            red = green = blue = alpha = 0;
            return false;
        }

        return _cpal.TryGetColor(Data.RawBytes.AsSpan(_cpalOffset, _cpalLength), palette, entry, out red, out green, out blue, out alpha);
    }

    /// <summary>Tells the backgrounds a palette is made for.</summary>
    public void GetPaletteType(int palette, out bool isForLightBackground, out bool isForDarkBackground)
    {
        isForLightBackground = isForDarkBackground = false;
        _cpal?.GetPaletteType(Data.RawBytes.AsSpan(_cpalOffset, _cpalLength), palette, out isForLightBackground, out isForDarkBackground);
    }

    /// <summary>Tells whether a glyph is drawn with its own colors.</summary>
    public bool IsColorGlyph(int glyphId)
        => _colr is not null && (uint)glyphId < (uint)GlyphCount && _colr.HasGlyph(Data.RawBytes.AsSpan(_colrOffset, _colrLength), glyphId);

    /// <summary>Builds the display list of a color glyph, which validates what the glyph refers to.</summary>
    /// <param name="glyphId">The glyph.</param>
    /// <param name="palette">The palette its colors come from; a palette the font does not have is its first one.</param>
    /// <param name="scratch">The working storage of the outlines.</param>
    /// <param name="budget">The complexity budget of the glyph.</param>
    /// <returns>The display list, or <see langword="null"/> when the glyph has no colors of its own.</returns>
    /// <exception cref="InvalidFontException">The data of the glyph is malformed.</exception>
    /// <exception cref="FontResourceLimitException">The glyph exceeds the complexity limit or the allocation limit.</exception>
    public ColorGlyph? BuildColorGlyph(int glyphId, int palette, GlyphScratch scratch, ref GlyphBudget budget)
    {
        if (_colr is null || (uint)glyphId >= (uint)GlyphCount)
            return null;

        return _colr.Build(Data.RawBytes.AsSpan(_colrOffset, _colrLength), this, glyphId, (uint)palette < (uint)PaletteCount ? palette : 0, scratch, ref budget);
    }

    /// <summary>Gets a value indicating whether the font has color glyphs made of layers (version 0 of the <c>COLR</c> table).</summary>
    public bool HasColorLayers => _colr is { HasLayerRecords: true };

    /// <summary>Tells whether a glyph is a color glyph made of layers (version 0 of the <c>COLR</c> table).</summary>
    public bool IsLayeredColorGlyph(int glyphId)
        => _colr is not null && (uint)glyphId < (uint)GlyphCount && _colr.HasLayers(Data.RawBytes.AsSpan(_colrOffset, _colrLength), glyphId);

    /// <summary>
    /// Gets the layers of a color glyph of version 0 of the <c>COLR</c> table, from the bottom one: the glyph whose
    /// outline each layer fills and the index of its color in the palette.
    /// </summary>
    /// <returns><see langword="null"/> when the glyph has no layers; a glyph of version 1 has paints, see <see cref="BuildColorGlyph"/>.</returns>
    /// <exception cref="InvalidFontException">The layers of the glyph are malformed.</exception>
    public ColorLayer[]? GetColorLayers(int glyphId)
        => _colr is null || (uint)glyphId >= (uint)GlyphCount ? null : _colr.GetLayers(Data.RawBytes.AsSpan(_colrOffset, _colrLength), glyphId);

    /// <summary>Gets a value indicating whether the font has a <c>MATH</c> table.</summary>
    public bool HasMathTable => _math is not null;

    /// <summary>Gets a value indicating whether the <c>MATH</c> table of the font has constants.</summary>
    public bool HasMathConstants => _math is { HasConstants: true };

    /// <summary>Gets a constant of mathematical layout, with the delta of the instance of a variable font; 0 when the font has none.</summary>
    /// <exception cref="InvalidFontException">The value is malformed.</exception>
    public int GetMathConstant(MathConstant constant) => _math?.GetConstant(Data.RawBytes.AsSpan(_mathOffset, _mathLength), this, constant) ?? 0;

    /// <summary>Gets the italics correction of a glyph of mathematical layout, in font units.</summary>
    /// <returns><see langword="false"/> when the font gives none for the glyph.</returns>
    /// <exception cref="InvalidFontException">The subtable is malformed.</exception>
    public bool TryGetMathItalicsCorrection(int glyphId, out int value)
    {
        value = 0;
        return _math is not null && _math.TryGetItalicsCorrection(Data.RawBytes.AsSpan(_mathOffset, _mathLength), this, glyphId, out value);
    }

    /// <summary>Gets where an accent above a glyph of mathematical layout is centered, in font units from its origin.</summary>
    /// <returns><see langword="false"/> when the font gives none for the glyph.</returns>
    /// <exception cref="InvalidFontException">The subtable is malformed.</exception>
    public bool TryGetMathTopAccentAttachment(int glyphId, out int value)
    {
        value = 0;
        return _math is not null && _math.TryGetTopAccentAttachment(Data.RawBytes.AsSpan(_mathOffset, _mathLength), this, glyphId, out value);
    }

    /// <summary>Tells whether a glyph of mathematical layout is an extended shape, such as a large operator.</summary>
    /// <exception cref="InvalidFontException">The subtable is malformed.</exception>
    public bool IsMathExtendedShape(int glyphId) => _math is not null && _math.IsExtendedShape(Data.RawBytes.AsSpan(_mathOffset, _mathLength), glyphId);

    /// <summary>Gets how much two parts of the assembly of a glyph of mathematical layout overlap at least, in font units.</summary>
    /// <exception cref="InvalidFontException">The subtable is malformed.</exception>
    public int MathMinConnectorOverlap => _math?.GetMinConnectorOverlap(Data.RawBytes.AsSpan(_mathOffset, _mathLength)) ?? 0;

    /// <summary>Gets how a glyph of mathematical layout grows along a direction: its larger forms and its assembly.</summary>
    /// <param name="glyphId">The glyph.</param>
    /// <param name="isVertical"><see langword="true"/> for the forms that are taller, <see langword="false"/> for the wider ones.</param>
    /// <returns><see langword="null"/> when the glyph does not grow in the direction.</returns>
    /// <exception cref="InvalidFontException">The subtables of the glyph are malformed.</exception>
    public MathGlyphConstruction? GetMathGlyphConstruction(int glyphId, bool isVertical)
        => _math?.GetConstruction(Data.RawBytes.AsSpan(_mathOffset, _mathLength), this, glyphId, isVertical);

    /// <summary>Gets the axes of a variable font; empty for a font that does not vary.</summary>
    public ReadOnlySpan<FontAxisRecord> Axes => _fvar is null ? default : _fvar.Axes;

    /// <summary>Gets the named instances of a variable font.</summary>
    public ReadOnlySpan<FontInstanceRecord> NamedInstances => _fvar is null ? default : _fvar.Instances;

    /// <summary>Gets the instance of a variable font the face is, or <see langword="null"/> for the default instance.</summary>
    public FontInstance? Instance => _instance;

    /// <summary>Gets a name of the font by its identifier in the <c>name</c> table.</summary>
    public string? GetName(int nameId)
        => TryGetTable(FontTags.Name, out var name) && name.Length >= 6 ? NameTable.Find(name.Span, nameId) : null;

    /// <summary>Gets the values a name of the font has in every language of the <c>name</c> table, each one once.</summary>
    /// <exception cref="InvalidFontException">The table is malformed.</exception>
    public IReadOnlyList<string> GetNames(int nameId)
    {
        var names = new List<string>();
        if (TryGetTable(FontTags.Name, out var name) && name.Length >= 6)
        {
            NameTable.FindAll(name.Span, nameId, names);
        }

        return names;
    }

    /// <summary>Gets the positions of the carets inside a ligature glyph, in font units along its advance, from the <c>GDEF</c> table.</summary>
    /// <param name="glyphId">The ligature glyph.</param>
    /// <param name="carets">Receives the positions, in the order of the components of the ligature.</param>
    /// <returns>The number of carets the font gives for the glyph; 0 when it gives none.</returns>
    /// <exception cref="InvalidFontException">The list of the carets is malformed.</exception>
    public int GetLigatureCarets(int glyphId, Span<int> carets)
        => Gdef is null || (uint)glyphId >= (uint)GlyphCount ? 0 : Gdef.GetLigatureCarets(GdefData.Span, this, glyphId, carets);

    /// <summary>
    /// Gets the delta of a value of the layout tables for the instance, from the item variation store of the GDEF
    /// table, rounded to a font unit; 0 for the default instance.
    /// </summary>
    public int GetLayoutDelta(int outer, int inner)
    {
        if (_instance is null || Gdef is not { VariationStore: not 0 } gdef)
            return 0;

        return ItemVariationStore.Round(ItemVariationStore.GetDelta(GdefData.Span, gdef.VariationStore, outer, inner, _instance.LayoutScalars));
    }

    /// <summary>Gets the face of an instance of a variable font.</summary>
    /// <param name="userCoordinates">
    /// The value of each axis, in the order of the axes, as 16.16 fixed-point numbers; an axis without value is at its
    /// default. A value outside the range of its axis is brought to the nearest end.
    /// </param>
    /// <returns>The face of the instance; the face of the default instance when every axis is at its default.</returns>
    public FontFace WithCoordinates(ReadOnlySpan<int> userCoordinates)
    {
        var root = _default;
        if (root._fvar is null)
            return root;

        var axes = root._fvar.Axes;
        var user = new int[axes.Length];
        var coordinates = new int[axes.Length];
        var isDefault = true;
        for (var i = 0; i < axes.Length; i++)
        {
            user[i] = i < userCoordinates.Length ? Math.Clamp(userCoordinates[i], axes[i].Minimum, axes[i].Maximum) : axes[i].Default;
            coordinates[i] = FontInstance.Normalize(axes[i], user[i]);
            isDefault &= user[i] == axes[i].Default;
        }

        if (isDefault)
            return root;

        // "Coordinate scales and normalization": the values are normalized as 16.16 numbers, mapped by the avar table,
        // then rounded to 2.14 numbers, which version 2 of the table changes with the deltas of its variation store.
        var bytes = Data.RawBytes.AsSpan();
        var avar = bytes.Slice(root._avarOffset, root._avarLength);
        AvarTable.Map(avar, coordinates);
        for (var i = 0; i < coordinates.Length; i++)
        {
            coordinates[i] = FontInstance.ToTableValue(coordinates[i]);
        }

        AvarTable.AddDeltas(avar, coordinates);

        var instance = new FontInstance(user, coordinates);
        if (root._hvarStore != 0)
        {
            instance.AdvanceScalars = ItemVariationStore.ComputeScalars(bytes.Slice(root._hvarOffset, root._hvarLength), root._hvarStore, coordinates);
        }

        if (root._vvarStore != 0)
        {
            instance.VerticalScalars = ItemVariationStore.ComputeScalars(bytes.Slice(root._vvarOffset, root._vvarLength), root._vvarStore, coordinates);
        }

        if (root._base.VariationStore != 0)
        {
            instance.BaselineScalars = ItemVariationStore.ComputeScalars(bytes.Slice(root._baseOffset, root._baseLength), root._base.VariationStore, coordinates);
        }

        if (root.Gdef is { VariationStore: not 0 } gdef)
        {
            instance.LayoutScalars = ItemVariationStore.ComputeScalars(root.GdefData.Span, gdef.VariationStore, coordinates);
        }

        if (root._colr is { VariationStore: not 0 } colr)
        {
            instance.ColorScalars = ItemVariationStore.ComputeScalars(bytes.Slice(root._colrOffset, root._colrLength), colr.VariationStore, coordinates);
        }

        if (root._cff is { VariationStore: not 0 } cff)
        {
            instance.OutlineScalars = ItemVariationStore.ComputeScalars(bytes.Slice(root._cffOffset, root._cffLength), cff.VariationStore, coordinates);
        }

        var face = (FontFace)root.MemberwiseClone();
        face._instance = instance;
        face.ApplyMetricVariations(instance);
        return face;
    }

    /// <summary>
    /// Varies the metrics of the font: OpenType specification, "MVAR - Metrics Variations Table". The table gives a
    /// delta to values of the OS/2, hhea and post tables, each named by a tag.
    /// </summary>
    private void ApplyMetricVariations(FontInstance instance)
    {
        // The weight class is not a metric: an instance says its weight with the axis of weights.
        var axes = Axes;
        for (var i = 0; i < axes.Length; i++)
        {
            if (axes[i].Tag == FontTags.WeightAxis)
            {
                Weight = Math.Clamp((instance.UserCoordinates[i] + 0x8000) >> 16, 1, 1000);
            }
        }

        var mvar = Data.RawBytes.AsSpan(_mvarOffset, _mvarLength);
        if (mvar.Length < 12 || FontBinary.UInt16(mvar, 0) != 1)
            return;

        var recordSize = FontBinary.UInt16(mvar, 6);
        var recordCount = FontBinary.UInt16(mvar, 8);
        var store = FontBinary.UInt16(mvar, 10);
        if (store == 0 || recordSize < 8)
            return;

        var records = FontBinary.Slice(mvar, 12, (long)recordCount * recordSize);
        var scalars = ItemVariationStore.ComputeScalars(mvar, store, instance.Coordinates);
        for (var i = 0; i < recordCount; i++)
        {
            var record = records.Slice(i * recordSize, 8);
            var delta = ItemVariationStore.Round(ItemVariationStore.GetDelta(mvar, store, FontBinary.UInt16(record, 4), FontBinary.UInt16(record, 6), scalars));
            if (delta == 0)
                continue;

            // The ascender, the descender and the line gap have one tag for the tables that both give them.
            switch (FontBinary.UInt32(record, 0))
            {
                case FontTags.MetricAscender:
                    TypoAscender += delta;
                    HorizontalAscender += delta;
                    break;

                case FontTags.MetricDescender:
                    TypoDescender += delta;
                    HorizontalDescender += delta;
                    break;

                case FontTags.MetricLineGap:
                    TypoLineGap += delta;
                    HorizontalLineGap += delta;
                    break;

                case FontTags.MetricWinAscent:
                    WinAscent += delta;
                    break;

                case FontTags.MetricWinDescent:
                    WinDescent += delta;
                    break;

                case FontTags.MetricXHeight:
                    XHeight += delta;
                    break;

                case FontTags.MetricCapHeight:
                    CapHeight += delta;
                    break;

                case FontTags.MetricStrikeoutSize:
                    StrikeoutSize += delta;
                    break;

                case FontTags.MetricStrikeoutOffset:
                    StrikeoutPosition += delta;
                    break;

                case FontTags.MetricUnderlineSize:
                    UnderlineThickness += delta;
                    break;

                case FontTags.MetricUnderlineOffset:
                    UnderlinePosition += delta;
                    break;

                case FontTags.MetricSubscriptYOffset:
                    SubscriptYOffset += delta;
                    break;

                case FontTags.MetricSuperscriptYOffset:
                    SuperscriptYOffset += delta;
                    break;

                case FontTags.MetricVerticalAscender:
                    VerticalAscender += delta;
                    break;

                case FontTags.MetricVerticalDescender:
                    VerticalDescender += delta;
                    break;

                case FontTags.MetricVerticalLineGap:
                    VerticalLineGap += delta;
                    break;
            }
        }
    }

    /// <summary>Gets where the <c>glyf</c> table starts in the bytes of the font.</summary>
    internal int GlyfOffset => _glyfOffset;

    /// <summary>Finds a table in the bytes of the font.</summary>
    internal bool TryFindTable(uint tag, out int offset, out int length) => _directory.TryFind(tag, out offset, out length);

    internal GlyfContext CreateGlyfContext()
    {
        var bytes = Data.RawBytes.AsSpan();
        var varies = _instance is not null && _gvar is not null;
        return new GlyfContext
        {
            Loca = bytes.Slice(_locaOffset, _locaLength),
            Glyf = bytes.Slice(_glyfOffset, _glyfLength),
            Hmtx = bytes.Slice(_hmtxOffset, _hmtxLength),
            HasLongLocations = _hasLongLocations,
            GlyphCount = GlyphCount,
            MetricCount = _horizontalMetricCount,
            Variations = varies ? _gvar : null,
            Gvar = varies ? bytes.Slice(_gvarOffset, _gvarLength) : default,
            Coordinates = varies ? _instance!.Coordinates : default,
        };
    }

    /// <summary>Gets a value indicating whether the font has pair adjustments in its legacy kerning table.</summary>
    public bool HasLegacyKerning => _kern is not null;

    /// <summary>
    /// Gets the adjustment the legacy kerning table gives to the advance of a glyph when another glyph follows it, in
    /// font units; 0 when the font has no such table or no adjustment for the pair.
    /// </summary>
    public int GetLegacyKerning(int left, int right)
    {
        if (_kern is null || (uint)left >= (uint)GlyphCount || (uint)right >= (uint)GlyphCount)
            return 0;

        return _kern.GetAdjustment(Data.RawBytes.AsSpan(_kernOffset, _kernLength), left, right);
    }

    /// <summary>Decodes the outline of a glyph into a sink. A glyph without outline, such as a space, emits nothing.</summary>
    /// <exception cref="InvalidFontException">The data of the glyph is malformed.</exception>
    /// <exception cref="UnsupportedFontFeatureException">The outlines of the font are in a format that is not supported.</exception>
    /// <exception cref="FontResourceLimitException">The glyph exceeds the complexity limit or the allocation limit.</exception>
    public void DecodeOutline(int glyphId, OutlineSink sink, GlyphScratch scratch, ref GlyphBudget budget)
    {
        if ((uint)glyphId >= (uint)GlyphCount)
            return;

        switch (Outlines)
        {
            case FontOutlineKind.TrueType:
                GlyfOutlineReader.Decode(CreateGlyfContext(), glyphId, sink, scratch, ref budget);
                break;

            case FontOutlineKind.Cff:
                _cff!.Decode(Data.RawBytes.AsSpan(_cffOffset, _cffLength), glyphId, sink, ref budget);
                break;

            case FontOutlineKind.Cff2:
                _cff!.Decode(Data.RawBytes.AsSpan(_cffOffset, _cffLength), glyphId, sink, ref budget, _instance is null ? default : _instance.OutlineScalars);
                break;
        }
    }

    /// <summary>
    /// Decodes the outline of a glyph as it is drawn at a size: the outline of the glyph, or, when the font was loaded
    /// to use its bitmaps without colors and has one for the glyph at exactly this size, the squares of its pixels.
    /// </summary>
    /// <param name="glyphId">The glyph.</param>
    /// <param name="size">The size the glyph is drawn at, in pixels per em.</param>
    /// <param name="sink">The sink.</param>
    /// <param name="scratch">The working storage of the outlines.</param>
    /// <param name="budget">The budget of the glyph.</param>
    /// <exception cref="InvalidFontException">The data of the glyph is malformed.</exception>
    /// <exception cref="UnsupportedFontFeatureException">The outlines of the font are in a format that is not supported.</exception>
    /// <exception cref="FontResourceLimitException">The glyph exceeds the complexity limit or the allocation limit.</exception>
    public void DecodeOutline(int glyphId, double size, OutlineSink sink, GlyphScratch scratch, ref GlyphBudget budget)
    {
        if (!TryDecodeMonochromeBitmap(glyphId, size, sink, ref budget))
        {
            DecodeOutline(glyphId, sink, scratch, ref budget);
        }
    }

    /// <summary>
    /// Emits the squares of the pixels of the bitmap without colors the font has for a glyph at exactly a size, when
    /// it was loaded to use them.
    /// </summary>
    /// <returns><see langword="true"/> when the glyph is drawn with a bitmap.</returns>
    internal bool TryDecodeMonochromeBitmap(int glyphId, double size, OutlineSink sink, ref GlyphBudget budget)
    {
        if (_eblc is not null && (uint)glyphId < (uint)GlyphCount && size is >= 1 and <= 255 && size == Math.Floor(size))
        {
            var bytes = Data.RawBytes.AsSpan();
            var bits = bytes.Slice(_ebdtOffset, _ebdtLength);
            if (_eblc.TryGetMonochromeGlyph(bytes.Slice(_eblcOffset, _eblcLength), bits, glyphId, (int)size, out var bitmap))
            {
                EmitBitmap(bits.Slice(bitmap.Offset, bitmap.Length), bitmap, sink, ref budget);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Emits a bitmap as an outline: a rectangle for each run of pixels of each row, in font units. The rectangles of
    /// two rows share an edge and do not overlap: filled, they cover exactly the pixels of the bitmap.
    /// </summary>
    private void EmitBitmap(ReadOnlySpan<byte> bits, in MonochromeBitmap bitmap, OutlineSink sink, ref GlyphBudget budget)
    {
        var unit = (double)UnitsPerEm / bitmap.PixelsPerEm;
        var rowBits = bitmap.IsByteAligned ? ((bitmap.Width + 7) / 8) * 8 : bitmap.Width;
        for (var row = 0; row < bitmap.Height; row++)
        {
            budget.Charge(1);
            var top = (bitmap.Top - row) * unit;
            var bottom = (bitmap.Top - row - 1) * unit;
            var first = row * rowBits;
            for (var column = 0; column < bitmap.Width;)
            {
                if (!IsSet(bits, first + column))
                {
                    column++;
                    continue;
                }

                var end = column + 1;
                while (end < bitmap.Width && IsSet(bits, first + end))
                {
                    end++;
                }

                // In the direction of the contours of a TrueType outline.
                budget.Charge(1);
                var left = (bitmap.Left + column) * unit;
                var right = (bitmap.Left + end) * unit;
                sink.MoveTo(left, bottom);
                sink.LineTo(left, top);
                sink.LineTo(right, top);
                sink.LineTo(right, bottom);
                sink.LineTo(left, bottom);
                sink.Close();
                column = end;
            }
        }

        static bool IsSet(ReadOnlySpan<byte> bits, int index) => (bits[index >> 3] & (0x80 >> (index & 7))) != 0;
    }

    private ReadOnlySpan<byte> GetRequiredTable(uint tag, int minimumLength)
    {
        if (!_directory.TryFind(tag, out var offset, out var length))
            throw FontErrors.InvalidTable(tag, "is missing.");

        if (length < minimumLength)
            throw FontErrors.InvalidTable(tag, "is truncated.");

        return Data.RawBytes.AsSpan(offset, length);
    }
}
