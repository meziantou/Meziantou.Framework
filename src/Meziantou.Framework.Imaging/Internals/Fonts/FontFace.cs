namespace Meziantou.Framework.Imaging.Internals;

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
internal sealed class FontFace
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
    private readonly CffTable? _cff;
    private readonly int _cffOffset;
    private readonly int _cffLength;

    public FontFace(FontData data, long directoryOffset)
    {
        Data = data;
        var file = data.Bytes.AsSpan();
        _directory = SfntDirectory.Read(file, directoryOffset);

        // head: OpenType specification, "head - Font Header Table".
        var head = GetRequiredTable(FontTags.Head, 54);
        if (FontBinary.UInt32(head, 12) != 0x5F0F3CF5)
            throw FontErrors.InvalidTable(FontTags.Head, "does not have its magic number.");

        UnitsPerEm = FontBinary.UInt16(head, 18);
        if (UnitsPerEm is < 16 or > 16384)
            throw FontErrors.InvalidTable(FontTags.Head, $"declares {UnitsPerEm} units per em; the valid range is 16 to 16384.");

        var macStyle = FontBinary.UInt16(head, 44);
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

        // OS/2: optional in old fonts. Its fields were added by versions; a field beyond the table is absent.
        if (TryGetTable(FontTags.Os2, out var os2Memory) && os2Memory.Length >= 78)
        {
            var os2 = os2Memory.Span;
            HasOs2 = true;
            Weight = FontBinary.UInt16(os2, 4);
            StrikeoutSize = FontBinary.Int16(os2, 26);
            StrikeoutPosition = FontBinary.Int16(os2, 28);
            var selection = FontBinary.UInt16(os2, 62);
            IsItalic = (selection & 0x0001) != 0;
            UseTypoMetrics = (selection & 0x0080) != 0;
            TypoAscender = FontBinary.Int16(os2, 68);
            TypoDescender = FontBinary.Int16(os2, 70);
            TypoLineGap = FontBinary.Int16(os2, 72);
            WinAscent = FontBinary.UInt16(os2, 74);
            WinDescent = FontBinary.UInt16(os2, 76);
            if (FontBinary.UInt16(os2, 0) >= 2 && os2.Length >= 90)
            {
                XHeight = FontBinary.Int16(os2, 86);
                CapHeight = FontBinary.Int16(os2, 88);
            }
        }
        else
        {
            Weight = (macStyle & 0x0001) != 0 ? 700 : 400;
            IsItalic = (macStyle & 0x0002) != 0;
        }

        // post: the underline, and whether the font is monospaced.
        if (TryGetTable(FontTags.Post, out var postMemory) && postMemory.Length >= 16)
        {
            var post = postMemory.Span;
            HasPost = true;
            UnderlinePosition = FontBinary.Int16(post, 8);
            UnderlineThickness = FontBinary.Int16(post, 10);
            IsMonospaced = FontBinary.UInt32(post, 12) != 0;
        }

        if (TryGetTable(FontTags.Name, out var nameMemory) && nameMemory.Length >= 6)
        {
            var name = nameMemory.Span;
            FamilyName = NameTable.Find(name, NameTable.TypographicFamilyName) ?? NameTable.Find(name, NameTable.FamilyName) ?? "";
            SubfamilyName = NameTable.Find(name, NameTable.TypographicSubfamilyName) ?? NameTable.Find(name, NameTable.SubfamilyName) ?? "";
            PostScriptName = NameTable.Find(name, NameTable.PostScriptName);
        }
        else
        {
            FamilyName = "";
            SubfamilyName = "";
        }

        if (!TryGetTable(FontTags.Cmap, out var cmap) || cmap.Length < 4)
            throw FontErrors.InvalidTable(FontTags.Cmap, "is missing.");

        _cmap = CmapTable.Read(cmap, GlyphCount);

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
        else if (_directory.TryFind(FontTags.Cff2, out _, out _))
        {
            Outlines = FontOutlineKind.Cff2;
        }
        else
        {
            Outlines = FontOutlineKind.None;
        }
    }

    public FontData Data { get; }

    public int UnitsPerEm { get; }

    public int GlyphCount { get; }

    public FontOutlineKind Outlines { get; }

    public string FamilyName { get; }

    public string SubfamilyName { get; }

    public string? PostScriptName { get; }

    public int Weight { get; }

    public bool IsItalic { get; }

    public bool IsMonospaced { get; }

    public int HorizontalAscender { get; }

    public int HorizontalDescender { get; }

    public int HorizontalLineGap { get; }

    public bool HasOs2 { get; }

    public bool UseTypoMetrics { get; }

    public int TypoAscender { get; }

    public int TypoDescender { get; }

    public int TypoLineGap { get; }

    public int WinAscent { get; }

    public int WinDescent { get; }

    public int XHeight { get; }

    public int CapHeight { get; }

    public int StrikeoutPosition { get; }

    public int StrikeoutSize { get; }

    public bool HasPost { get; }

    public int UnderlinePosition { get; }

    public int UnderlineThickness { get; }

    /// <summary>Gets the tags of the tables of the face, in increasing order.</summary>
    public ReadOnlySpan<uint> TableTags => _directory.Tags;

    /// <summary>Gets a table of the face.</summary>
    /// <returns><see langword="false"/> when the face does not have the table.</returns>
    public bool TryGetTable(uint tag, out ReadOnlyMemory<byte> table)
    {
        if (_directory.TryFind(tag, out var offset, out var length))
        {
            table = Data.Bytes.AsMemory(offset, length);
            return true;
        }

        table = default;
        return false;
    }

    /// <summary>Gets the glyph of a character, or 0 when the font has none.</summary>
    public int MapCodePoint(int codePoint) => _cmap.Map(codePoint);

    /// <summary>Gets the glyph of a variation sequence.</summary>
    public CmapVariation MapVariation(int codePoint, int selector, out int glyphId) => _cmap.MapVariation(codePoint, selector, out glyphId);

    /// <summary>Gets the advance width of a glyph, in font units.</summary>
    public int GetAdvanceWidth(int glyphId)
    {
        if ((uint)glyphId >= (uint)GlyphCount)
            return 0;

        // The glyphs after the last metric share its advance.
        var index = Math.Min(glyphId, _horizontalMetricCount - 1);
        return FontBinary.UInt16(Data.Bytes.AsSpan(_hmtxOffset, _hmtxLength), index * 4);
    }

    /// <summary>Decodes the outline of a glyph into a sink. A glyph without outline, such as a space, emits nothing.</summary>
    /// <exception cref="InvalidImageContentException">The data of the glyph is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The outlines of the font are in a format that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">The glyph exceeds the complexity limit or the allocation limit.</exception>
    public void DecodeOutline(int glyphId, OutlineSink sink, GlyphScratch scratch, ref GlyphBudget budget)
    {
        if ((uint)glyphId >= (uint)GlyphCount)
            return;

        switch (Outlines)
        {
            case FontOutlineKind.TrueType:
                var bytes = Data.Bytes.AsSpan();
                GlyfOutlineReader.Decode(bytes.Slice(_locaOffset, _locaLength), bytes.Slice(_glyfOffset, _glyfLength), _hasLongLocations, GlyphCount, glyphId, sink, scratch, ref budget);
                break;

            case FontOutlineKind.Cff:
                _cff!.Decode(Data.Bytes.AsSpan(_cffOffset, _cffLength), glyphId, sink, ref budget);
                break;

            case FontOutlineKind.Cff2:
                throw FontErrors.Unsupported("The font has CFF2 outlines, which are not supported.", "CFF2 outlines");
        }
    }

    private ReadOnlySpan<byte> GetRequiredTable(uint tag, int minimumLength)
    {
        if (!_directory.TryFind(tag, out var offset, out var length))
            throw FontErrors.InvalidTable(tag, "is missing.");

        if (length < minimumLength)
            throw FontErrors.InvalidTable(tag, "is truncated.");

        return Data.Bytes.AsSpan(offset, length);
    }
}
