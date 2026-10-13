namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The structure of a <c>CFF </c> table: where the charstrings of the glyphs and their subroutines are. Adobe Technical
/// Note #5176, "The Compact Font Format Specification".
/// </summary>
/// <remarks>
/// <para>
/// The header, the INDEX structures and the DICTs are validated when the table is read; a charstring is validated when
/// its glyph is decoded (<see cref="CffCharstringInterpreter"/>). The table of an OpenType font holds one font.
/// </para>
/// <para>
/// What is not needed to draw a glyph is not read: the names of the glyphs (charset, strings), the encoding, the
/// hinting values of the private DICTs and the font matrix. OpenType gives the units of the outlines in the <c>head</c>
/// table and the advances in the <c>hmtx</c> table.
/// </para>
/// </remarks>
internal sealed class CffTable
{
    // Operators of the Top DICT and of the Private DICT: tables 9, 10 and 23 of the specification.
    private const int CharStringsOperator = 17;
    private const int PrivateOperator = 18;
    private const int SubrsOperator = 19;
    private const int VariationStoreIndexOperator = 22;
    private const int VariationStoreOperator = 24;
    private const int CharstringTypeOperator = CffDictReader.Escape + 6;
    private const int RosOperator = CffDictReader.Escape + 30;
    private const int FdArrayOperator = CffDictReader.Escape + 36;
    private const int FdSelectOperator = CffDictReader.Escape + 37;

    private readonly CffIndex _charStrings;
    private readonly CffIndex _globalSubroutines;

    // The local subroutines of each font DICT: one for a font whose glyphs have names, one per font DICT for a font
    // whose glyphs are identified by CID.
    private readonly CffIndex[] _localSubroutines;
    private readonly int _fdSelect;
    private readonly int _fdSelectFormat;

    // CFF2: the subtable of the variation store that the charstrings of each font DICT blend with, unless they say
    // otherwise.
    private readonly int[]? _variationStoreIndexes;

    private CffTable(CffIndex charStrings, CffIndex globalSubroutines, CffIndex[] localSubroutines, int fdSelect, int fdSelectFormat, int variationStore = 0, int[]? variationStoreIndexes = null)
    {
        _charStrings = charStrings;
        _globalSubroutines = globalSubroutines;
        _localSubroutines = localSubroutines;
        _fdSelect = fdSelect;
        _fdSelectFormat = fdSelectFormat;
        VariationStore = variationStore;
        _variationStoreIndexes = variationStoreIndexes;
    }

    /// <summary>Gets a value indicating whether the table is a CFF2 table, whose charstrings have another set of operators.</summary>
    public bool IsCff2 => _variationStoreIndexes is not null;

    /// <summary>Gets the position of the item variation store of a CFF2 table; 0 when the table has none.</summary>
    public int VariationStore { get; }

    /// <summary>Gets the number of charstrings.</summary>
    public int GlyphCount => _charStrings.Count;

    /// <summary>Reads a <c>CFF</c> table, or a <c>CFF2</c> table: the tag of the table says which one it is.</summary>
    public static CffTable Read(ReadOnlySpan<byte> table, bool isCff2 = false)
    {
        // Header: major version, minor version, header size, offset size.
        var major = FontBinary.UInt8(table, 0);
        if (isCff2)
        {
            if (major != 2)
                throw FontErrors.InvalidTable(FontTags.Cff2, $"has the unknown version {major}.");

            return ReadCff2(table);
        }

        if (major != 1)
            throw FontErrors.InvalidTable(FontTags.Cff, $"has the unknown version {major}.");

        var headerSize = FontBinary.UInt8(table, 2);
        if (headerSize < 4)
            throw FontErrors.InvalidTable(FontTags.Cff, "has a header shorter than its four fields.");

        // The four INDEX structures that follow the header: names, Top DICTs, strings, global subroutines.
        var names = CffIndex.Read(table, headerSize);
        var topDicts = CffIndex.Read(table, names.End);
        var strings = CffIndex.Read(table, topDicts.End);
        var globalSubroutines = CffIndex.Read(table, strings.End);
        if (topDicts.Count != 1)
            throw FontErrors.InvalidTable(FontTags.Cff, $"holds {topDicts.Count} fonts; the table of an OpenType font holds one.");

        long charStringsOffset = -1;
        long fdArrayOffset = -1;
        long fdSelectOffset = -1;
        long privateOffset = -1;
        long privateLength = 0;
        var isCidKeyed = false;
        Span<double> operands = stackalloc double[CffDictReader.MaxOperands];
        var reader = new CffDictReader(topDicts.Get(table, 0));
        while (reader.TryRead(operands, out var count, out var @operator))
        {
            switch (@operator)
            {
                case CharStringsOperator when count >= 1:
                    charStringsOffset = ToOffset(operands[0]);
                    break;

                case PrivateOperator when count >= 2:
                    privateLength = ToOffset(operands[0]);
                    privateOffset = ToOffset(operands[1]);
                    break;

                case CharstringTypeOperator when count >= 1 && operands[0] != 2:
                    throw FontErrors.Unsupported("The font has Type 1 charstrings in its CFF table, which are not supported.", "Type 1 charstrings");

                case RosOperator:
                    isCidKeyed = true;
                    break;

                case FdArrayOperator when count >= 1:
                    fdArrayOffset = ToOffset(operands[0]);
                    break;

                case FdSelectOperator when count >= 1:
                    fdSelectOffset = ToOffset(operands[0]);
                    break;
            }
        }

        if (charStringsOffset < 0)
            throw FontErrors.InvalidTable(FontTags.Cff, "does not say where its charstrings are.");

        var charStrings = CffIndex.Read(table, charStringsOffset);
        if (charStrings.Count == 0)
            throw FontErrors.InvalidTable(FontTags.Cff, "has no charstring.");

        if (!isCidKeyed)
            return new CffTable(charStrings, globalSubroutines, [ReadLocalSubroutines(table, privateOffset, privateLength)], fdSelect: -1, fdSelectFormat: 0);

        // A CID-keyed font: an array of font DICTs, each with its private DICT, and the font DICT of each glyph.
        if (fdArrayOffset < 0 || fdSelectOffset < 0)
            throw FontErrors.InvalidTable(FontTags.Cff, "is CID-keyed and does not have its font DICT array or its font DICT selector.");

        var fontDicts = CffIndex.Read(table, fdArrayOffset);
        if (fontDicts.Count == 0)
            throw FontErrors.InvalidTable(FontTags.Cff, "has an empty font DICT array.");

        var localSubroutines = new CffIndex[fontDicts.Count];
        for (var i = 0; i < localSubroutines.Length; i++)
        {
            privateOffset = -1;
            privateLength = 0;
            reader = new CffDictReader(fontDicts.Get(table, i));
            while (reader.TryRead(operands, out var count, out var @operator))
            {
                if (@operator == PrivateOperator && count >= 2)
                {
                    privateLength = ToOffset(operands[0]);
                    privateOffset = ToOffset(operands[1]);
                }
            }

            localSubroutines[i] = ReadLocalSubroutines(table, privateOffset, privateLength);
        }

        return new CffTable(charStrings, globalSubroutines, localSubroutines, (int)fdSelectOffset, ReadFontDictSelectorFormat(table, fdSelectOffset, charStrings.Count));
    }

    /// <summary>
    /// Reads a CFF2 table: OpenType specification, "CFF2 - Compact Font Format (CFF) Version 2". The header is followed
    /// by one Top DICT and the global subroutines; there are no names nor strings, every font has an array of font
    /// DICTs, and the counts of the INDEX structures are 32 bits.
    /// </summary>
    private static CffTable ReadCff2(ReadOnlySpan<byte> table)
    {
        var headerSize = FontBinary.UInt8(table, 2);
        var topDictLength = FontBinary.UInt16(table, 3);
        if (headerSize < 5)
            throw FontErrors.InvalidTable(FontTags.Cff2, "has a header shorter than its fields.");

        var globalSubroutines = CffIndex.Read(table, (long)headerSize + topDictLength, isCff2: true);
        long charStringsOffset = -1;
        long fdArrayOffset = -1;
        long fdSelectOffset = -1;
        long variationStoreOffset = -1;
        Span<double> operands = stackalloc double[CffDictReader.MaxOperands];
        var reader = new CffDictReader(FontBinary.Slice(table, headerSize, topDictLength), isCff2: true);
        while (reader.TryRead(operands, out var count, out var @operator))
        {
            switch (@operator)
            {
                case CharStringsOperator when count >= 1:
                    charStringsOffset = ToOffset(operands[0]);
                    break;

                case VariationStoreOperator when count >= 1:
                    variationStoreOffset = ToOffset(operands[0]);
                    break;

                case FdArrayOperator when count >= 1:
                    fdArrayOffset = ToOffset(operands[0]);
                    break;

                case FdSelectOperator when count >= 1:
                    fdSelectOffset = ToOffset(operands[0]);
                    break;
            }
        }

        if (charStringsOffset < 0 || fdArrayOffset < 0)
            throw FontErrors.InvalidTable(FontTags.Cff2, "does not say where its charstrings or its font DICTs are.");

        var charStrings = CffIndex.Read(table, charStringsOffset, isCff2: true);
        if (charStrings.Count == 0)
            throw FontErrors.InvalidTable(FontTags.Cff2, "has no charstring.");

        var fontDicts = CffIndex.Read(table, fdArrayOffset, isCff2: true);
        if (fontDicts.Count == 0)
            throw FontErrors.InvalidTable(FontTags.Cff2, "has an empty font DICT array.");

        var localSubroutines = new CffIndex[fontDicts.Count];
        var variationStoreIndexes = new int[fontDicts.Count];
        for (var i = 0; i < localSubroutines.Length; i++)
        {
            long privateOffset = -1;
            long privateLength = 0;
            reader = new CffDictReader(fontDicts.Get(table, i), isCff2: true);
            while (reader.TryRead(operands, out var count, out var @operator))
            {
                if (@operator == PrivateOperator && count >= 2)
                {
                    privateLength = ToOffset(operands[0]);
                    privateOffset = ToOffset(operands[1]);
                }
            }

            if (privateOffset < 0 || privateLength == 0)
                continue;

            long subroutinesOffset = -1;
            reader = new CffDictReader(FontBinary.Slice(table, privateOffset, privateLength), isCff2: true);
            while (reader.TryRead(operands, out var count, out var @operator))
            {
                switch (@operator)
                {
                    case SubrsOperator when count >= 1:
                        subroutinesOffset = ToOffset(operands[0]);
                        break;

                    case VariationStoreIndexOperator when count >= 1:
                        variationStoreIndexes[i] = (int)ToOffset(operands[0]);
                        break;
                }
            }

            if (subroutinesOffset >= 0)
            {
                localSubroutines[i] = CffIndex.Read(table, privateOffset + subroutinesOffset, isCff2: true);
            }
        }

        // The selector is optional: without it, every glyph uses the first font DICT.
        var fdSelectFormat = 0;
        if (fdSelectOffset >= 0)
        {
            fdSelectFormat = ReadFontDictSelectorFormat(table, fdSelectOffset, charStrings.Count);
        }

        // The store follows its length, on 16 bits. A store of a format that is not known is left out: the glyphs then
        // do not vary.
        var variationStore = 0;
        if (variationStoreOffset >= 0 && variationStoreOffset <= table.Length - 10 && FontBinary.UInt16(table, (int)variationStoreOffset + 2) == 1)
        {
            variationStore = (int)variationStoreOffset + 2;
        }

        return new CffTable(charStrings, globalSubroutines, localSubroutines, (int)fdSelectOffset, fdSelectFormat, variationStore, variationStoreIndexes);
    }

    /// <summary>
    /// Validates the font DICT selector: section 19 of the CFF specification. Format 0 has one byte per glyph; formats
    /// 3 and 4 have ranges of glyphs, closed by a sentinel, with 16-bit and 32-bit glyph numbers.
    /// </summary>
    private static int ReadFontDictSelectorFormat(ReadOnlySpan<byte> table, long fdSelectOffset, int glyphCount)
    {
        var format = FontBinary.UInt8(table, (int)Math.Min(fdSelectOffset, int.MaxValue));
        switch (format)
        {
            case 0:
                FontBinary.EnsureRange(table, fdSelectOffset + 1, glyphCount);
                break;

            case 3:
                var rangeCount = FontBinary.UInt16(table, (int)fdSelectOffset + 1);
                FontBinary.EnsureRange(table, fdSelectOffset + 3, ((long)rangeCount * 3) + 2);
                break;

            case 4:
                var longRangeCount = FontBinary.UInt32(table, (int)fdSelectOffset + 1);
                FontBinary.EnsureRange(table, fdSelectOffset + 5, ((long)longRangeCount * 6) + 4);
                break;

            default:
                throw FontErrors.InvalidTable(FontTags.Cff, $"has a font DICT selector of the unknown format {format}.");
        }

        return format;
    }

    /// <summary>Decodes the outline of a glyph into a sink.</summary>
    /// <exception cref="InvalidImageContentException">The charstring of the glyph is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The charstring uses a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">The glyph exceeds the complexity limit.</exception>
    /// <summary>Decodes the outline of a glyph into a sink.</summary>
    /// <param name="table">The table.</param>
    /// <param name="glyphId">The glyph.</param>
    /// <param name="sink">The sink.</param>
    /// <param name="budget">The complexity budget of the glyph.</param>
    /// <param name="scalars">
    /// The scalars of the regions of the variation store of a CFF2 table for an instance of a variable font; empty for
    /// the default instance.
    /// </param>
    public void Decode(ReadOnlySpan<byte> table, int glyphId, OutlineSink sink, ref GlyphBudget budget, ReadOnlySpan<double> scalars = default)
    {
        if ((uint)glyphId >= (uint)_charStrings.Count)
            return;

        var fontDict = GetFontDict(table, glyphId);
        if ((uint)fontDict >= (uint)_localSubroutines.Length)
            throw FontErrors.InvalidTable(FontTags.Cff, "selects a font DICT that does not exist for a glyph.");

        var interpreter = _variationStoreIndexes is null
            ? new CffCharstringInterpreter(table, _globalSubroutines, _localSubroutines[fontDict], sink, ref budget)
            : new CffCharstringInterpreter(table, _globalSubroutines, _localSubroutines[fontDict], sink, ref budget, VariationStore, _variationStoreIndexes[fontDict], scalars);
        interpreter.Run(_charStrings.Get(table, glyphId));
    }

    private int GetFontDict(ReadOnlySpan<byte> table, int glyphId)
    {
        if (_fdSelect < 0)
            return 0;

        if (_fdSelectFormat == 0)
            return table[_fdSelect + 1 + glyphId];

        if (_fdSelectFormat == 4)
            return GetFontDictOfLongRanges(table, glyphId);

        // Format 3: the ranges are sorted by first glyph; the range of a glyph is the last one that starts at or before it.
        var ranges = table[(_fdSelect + 3)..];
        var low = 0;
        var high = FontBinary.UInt16(table, _fdSelect + 1) - 1;
        var result = -1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            if (FontBinary.UInt16(ranges, middle * 3) <= glyphId)
            {
                result = ranges[(middle * 3) + 2];
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return result;
    }

    /// <summary>Reads the private DICT of a font and gives its local subroutines, which the DICT places relative to itself.</summary>
    /// <summary>Format 4 of the selector: the ranges of format 3 with 32-bit glyph numbers and 16-bit font DICT numbers.</summary>
    private int GetFontDictOfLongRanges(ReadOnlySpan<byte> table, int glyphId)
    {
        var ranges = table[(_fdSelect + 5)..];
        long low = 0;
        var high = (long)FontBinary.UInt32(table, _fdSelect + 1) - 1;
        var result = -1;
        while (low <= high)
        {
            var middle = (int)((low + high) >>> 1);
            if (FontBinary.UInt32(ranges, middle * 6) <= (uint)glyphId)
            {
                result = FontBinary.UInt16(ranges, (middle * 6) + 4);
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return result;
    }

    private static CffIndex ReadLocalSubroutines(ReadOnlySpan<byte> table, long privateOffset, long privateLength)
    {
        if (privateOffset < 0 || privateLength == 0)
            return default;

        long subroutinesOffset = -1;
        Span<double> operands = stackalloc double[CffDictReader.MaxOperands];
        var reader = new CffDictReader(FontBinary.Slice(table, privateOffset, privateLength));
        while (reader.TryRead(operands, out var count, out var @operator))
        {
            if (@operator == SubrsOperator && count >= 1)
            {
                subroutinesOffset = ToOffset(operands[0]);
            }
        }

        return subroutinesOffset < 0 ? default : CffIndex.Read(table, privateOffset + subroutinesOffset);
    }

    private static long ToOffset(double value)
    {
        if (!(value >= 0 && value <= int.MaxValue) || value != Math.Floor(value))
            throw FontErrors.InvalidTable(FontTags.Cff, "has a DICT with an offset that is not a position of the table.");

        return (long)value;
    }
}
