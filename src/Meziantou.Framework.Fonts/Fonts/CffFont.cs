namespace Meziantou.Framework.Fonts;

/// <summary>
/// A font program in the Compact Font Format (Adobe Technical Note #5176) on its own, not in an OpenType font: a
/// <c>.cff</c> file, or a program embedded in a PDF document (<c>/FontFile3</c>, <c>Type1C</c> and <c>CIDFontType0C</c>).
/// It gives the names (or the CIDs) of the glyphs, the built-in encoding, the font matrix and the outlines.
/// </summary>
/// <remarks>
/// <para>
/// Only the first font of the program is read. The glyphs of a CID-keyed font are found by their CIDs, the glyphs of the
/// other fonts by their names or their codes. The <c>seac</c> operator (a base character and an accent, Adobe Technical
/// Note #5177, appendix C) is drawn from the glyphs the standard encoding gives to its codes.
/// </para>
/// <para>The object is immutable and can be used from several threads.</para>
/// </remarks>
public sealed class CffFont
{
    private const int CharsetOperator = 15;
    private const int EncodingOperator = 16;
    private const int RosOperator = CffDictReader.Escape + 30;
    private const int FontMatrixOperator = CffDictReader.Escape + 7;
    private const int FdArrayOperator = CffDictReader.Escape + 36;

    private static readonly Lazy<string?[]> StandardEncoding = new(CreateStandardEncoding);

    private readonly byte[] _table;
    private readonly CffTable _cff;
    private readonly string?[] _glyphNames;
    private readonly int[] _cids;
    private readonly Dictionary<string, int> _glyphsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<int, int> _glyphsByCid = [];
    private readonly int[] _builtinEncoding = new int[256];

    private CffFont(byte[] table)
    {
        _table = table;
        if (table.Length < 4)
            throw FontErrors.Truncated();

        _cff = CffTable.Read(table);

        var headerSize = FontBinary.UInt8(table, 2);
        var names = CffIndex.Read(table, headerSize);
        var topDicts = CffIndex.Read(table, names.End);
        var strings = CffIndex.Read(table, topDicts.End);
        if (topDicts.Count == 0)
            throw FontErrors.InvalidTable(FontTags.Cff, "has no Top DICT.");

        long charsetOffset = 0;
        long encodingOffset = 0;
        long fdArrayOffset = 0;
        double[]? topMatrix = null;
        Span<double> operands = stackalloc double[CffDictReader.MaxOperands];
        var reader = new CffDictReader(topDicts.Get(table, 0));
        while (reader.TryRead(operands, out var count, out var @operator))
        {
            switch (@operator)
            {
                case CharsetOperator when count >= 1:
                    charsetOffset = (long)operands[0];
                    break;
                case EncodingOperator when count >= 1:
                    encodingOffset = (long)operands[0];
                    break;
                case RosOperator:
                    IsCidKeyed = true;
                    break;
                case FontMatrixOperator when count >= 6:
                    topMatrix = operands[..6].ToArray();
                    break;
                case FdArrayOperator when count >= 1:
                    fdArrayOffset = (long)operands[0];
                    break;
            }
        }

        // The matrix of a CID-keyed font is the one of its font dictionary, then the one of the Top DICT (section 9): the
        // fonts put the scale in one of them
        var fdMatrix = IsCidKeyed && fdArrayOffset > 0 ? ReadFontDictMatrix(table, fdArrayOffset) : null;
        FontMatrix = Array.AsReadOnly((fdMatrix, topMatrix) switch
        {
            ({ } fd, { } top) => Multiply(fd, top),
            ({ } fd, null) => fd,
            (null, { } top) => top,
            _ => [0.001, 0, 0, 0.001, 0, 0],
        });

        GlyphCount = _cff.GlyphCount;

        // The charset: the name (or the CID) of each glyph after .notdef (section 13)
        _cids = ReadCharset(table, charsetOffset, GlyphCount);
        _glyphNames = new string?[GlyphCount];
        for (var glyph = 0; glyph < _cids.Length; glyph++)
        {
            if (IsCidKeyed)
            {
                _glyphsByCid.TryAdd(_cids[glyph], glyph);
            }
            else if (GetString(table, strings, _cids[glyph]) is { } name)
            {
                _glyphNames[glyph] = name;
                _glyphsByName.TryAdd(name, glyph);
            }
        }

        if (!IsCidKeyed)
        {
            ReadEncoding(table, encodingOffset, strings);
        }
    }

    /// <summary>Gets the number of glyphs of the font.</summary>
    public int GlyphCount { get; }

    /// <summary>Gets a value indicating whether the font is CID-keyed: its glyphs are found by their CIDs, not by their names.</summary>
    public bool IsCidKeyed { get; }

    /// <summary>
    /// Gets the matrix from the units of the outlines to text space, as the six values <c>a b c d e f</c>: the one of the
    /// font dictionary of a CID-keyed font multiplied by the one of the Top DICT, or <c>0.001 0 0 0.001 0 0</c> by default.
    /// </summary>
    public IReadOnlyList<double> FontMatrix { get; }

    /// <summary>Reads a CFF font program.</summary>
    /// <param name="data">The bytes of the program, starting with the CFF header. They are copied.</param>
    /// <returns>The font.</returns>
    /// <exception cref="InvalidFontException">The program is not a valid CFF program.</exception>
    public static CffFont Load(ReadOnlySpan<byte> data) => new(data.ToArray());

    /// <summary>Gets the name of a glyph in the charset of a font that is not CID-keyed.</summary>
    /// <param name="glyphId">The glyph.</param>
    /// <returns>The name, or <see langword="null"/> for a CID-keyed font or a glyph without a name.</returns>
    public string? GetGlyphName(int glyphId) => (uint)glyphId < (uint)_glyphNames.Length ? _glyphNames[glyphId] : null;

    /// <summary>Gets the glyph of a name in the charset of a font that is not CID-keyed.</summary>
    /// <param name="name">The name of the glyph, such as <c>A</c>.</param>
    /// <returns>The glyph, or 0 (<c>.notdef</c>) when the font has no glyph of that name.</returns>
    public int GetGlyphByName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _glyphsByName.GetValueOrDefault(name);
    }

    /// <summary>Gets the glyph of a CID: through the charset of a CID-keyed font, the CID itself otherwise.</summary>
    /// <param name="cid">The CID.</param>
    /// <returns>The glyph, or 0 (<c>.notdef</c>) when a CID-keyed font has no glyph for the CID.</returns>
    public int GetGlyphByCid(int cid) => IsCidKeyed ? _glyphsByCid.GetValueOrDefault(cid) : cid;

    /// <summary>Gets the glyph of a code in the built-in encoding of a font that is not CID-keyed (the standard encoding when the program has none).</summary>
    /// <param name="code">The code, from 0 to 255.</param>
    /// <returns>The glyph, or 0 (<c>.notdef</c>) when the code has none.</returns>
    public int GetGlyphByCode(int code) => code is >= 0 and < 256 ? _builtinEncoding[code] : 0;

    /// <summary>Decodes the outline of a glyph, in the units of the outlines (see <see cref="FontMatrix"/>). A glyph identifier that is not a glyph of the font draws nothing.</summary>
    /// <param name="glyphId">The glyph.</param>
    /// <param name="sink">The receiver of the contours.</param>
    /// <param name="budget">The limits of the work of the decoding (the operators of the charstrings, the subroutine calls).</param>
    /// <exception cref="InvalidFontException">The charstring of the glyph is malformed, or it exceeds the budget.</exception>
    public void DecodeOutline(int glyphId, OutlineSink sink, ref GlyphBudget budget)
    {
        ArgumentNullException.ThrowIfNull(sink);
        if ((uint)glyphId >= (uint)GlyphCount)
            return;

        if (_cff.DecodeWithAccentedGlyphs(_table, glyphId, sink, ref budget) is not { } accented || IsCidKeyed)
            return;

        // A glyph made of a base character and an accent, by their names in the standard encoding; the components cannot
        // be accented glyphs themselves
        var encoding = StandardEncoding.Value;
        if (encoding[accented.BaseCode & 0xFF] is { } baseName && _glyphsByName.TryGetValue(baseName, out var baseGlyph))
        {
            _cff.Decode(_table, baseGlyph, sink, ref budget);
        }

        if (encoding[accented.AccentCode & 0xFF] is { } accentName && _glyphsByName.TryGetValue(accentName, out var accentGlyph))
        {
            sink.Close();
            _cff.Decode(_table, accentGlyph, new OffsetSink(sink, accented.AccentX, accented.AccentY), ref budget);
        }
    }

    /// <summary>The standard encoding (Adobe Technical Note #5176, appendix B): the codes of the AFM file of Times-Roman.</summary>
    private static string?[] CreateStandardEncoding()
    {
        var names = new string?[256];
        foreach (var character in AfmFontMetrics.GetStandardFont("Times-Roman")!.CharacterMetrics)
        {
            if (character is { Code: >= 0 and < 256, Name: { } name })
            {
                names[character.Code] = name;
            }
        }

        return names;
    }

    /// <summary>Returns the product of two matrices <c>a b c d e f</c>: the transform of the first one, then the second one.</summary>
    private static double[] Multiply(double[] left, double[] right)
    {
        return
        [
            (left[0] * right[0]) + (left[1] * right[2]),
            (left[0] * right[1]) + (left[1] * right[3]),
            (left[2] * right[0]) + (left[3] * right[2]),
            (left[2] * right[1]) + (left[3] * right[3]),
            (left[4] * right[0]) + (left[5] * right[2]) + right[4],
            (left[4] * right[1]) + (left[5] * right[3]) + right[5],
        ];
    }

    /// <summary>Reads the font matrix of the first font dictionary of a CID-keyed font; <see langword="null"/> when it has none.</summary>
    private static double[]? ReadFontDictMatrix(byte[] table, long offset)
    {
        if (offset >= table.Length)
            return null;

        var fontDicts = CffIndex.Read(table, (int)offset);
        if (fontDicts.Count == 0)
            return null;

        Span<double> operands = stackalloc double[CffDictReader.MaxOperands];
        var reader = new CffDictReader(fontDicts.Get(table, 0));
        while (reader.TryRead(operands, out var count, out var @operator))
        {
            if (@operator == FontMatrixOperator && count >= 6)
                return operands[..6].ToArray();
        }

        return null;
    }

    private static string? GetString(byte[] table, CffIndex strings, int sid)
    {
        if (sid < CffStandardStrings.Names.Length)
            return CffStandardStrings.Names[sid];

        var index = sid - CffStandardStrings.Names.Length;
        if (index >= strings.Count)
            return null;

        return System.Text.Encoding.Latin1.GetString(strings.Get(table, index));
    }

    /// <summary>Reads the charset (section 13): the identifier of each glyph, 0 for .notdef.</summary>
    private static int[] ReadCharset(byte[] table, long offset, int glyphCount)
    {
        var result = new int[glyphCount];
        if (offset is 0 or 1 or 2)
        {
            // The predefined charsets: ISOAdobe has the identifiers of the glyphs in order; the expert charsets are not
            // used by the fonts of PDF documents
            for (var glyph = 0; glyph < glyphCount; glyph++)
            {
                result[glyph] = glyph;
            }

            return result;
        }

        var position = (int)offset;
        var format = FontBinary.UInt8(table, position++);
        var index = 1;
        switch (format)
        {
            case 0:
                while (index < glyphCount)
                {
                    result[index++] = FontBinary.UInt16(table, position);
                    position += 2;
                }

                break;
            case 1 or 2:
                while (index < glyphCount)
                {
                    var first = FontBinary.UInt16(table, position);
                    var left = format == 1 ? FontBinary.UInt8(table, position + 2) : FontBinary.UInt16(table, position + 2);
                    position += format == 1 ? 3 : 4;
                    for (var i = 0; i <= left && index < glyphCount; i++)
                    {
                        result[index++] = first + i;
                    }
                }

                break;
            default:
                throw FontErrors.InvalidTable(FontTags.Cff, $"has a charset of the unknown format {format}.");
        }

        return result;
    }

    /// <summary>Reads the encoding (section 12): the code of each glyph, and the supplements.</summary>
    private void ReadEncoding(byte[] table, long offset, CffIndex strings)
    {
        if (offset is 0 or 1)
        {
            // The standard encoding: the glyphs of the names of the codes
            var encoding = StandardEncoding.Value;
            for (var code = 0; code < 256; code++)
            {
                if (encoding[code] is { } name)
                {
                    _builtinEncoding[code] = _glyphsByName.GetValueOrDefault(name);
                }
            }

            return;
        }

        var position = (int)offset;
        var format = FontBinary.UInt8(table, position++);
        var glyph = 1;
        switch (format & 0x7F)
        {
            case 0:
            {
                var count = FontBinary.UInt8(table, position++);
                for (var i = 0; i < count && glyph < GlyphCount; i++)
                {
                    _builtinEncoding[FontBinary.UInt8(table, position++)] = glyph++;
                }

                break;
            }

            case 1:
            {
                var ranges = FontBinary.UInt8(table, position++);
                for (var i = 0; i < ranges; i++)
                {
                    var first = FontBinary.UInt8(table, position);
                    var left = FontBinary.UInt8(table, position + 1);
                    position += 2;
                    for (var j = 0; j <= left && glyph < GlyphCount && first + j < 256; j++)
                    {
                        _builtinEncoding[first + j] = glyph++;
                    }
                }

                break;
            }
        }

        if ((format & 0x80) != 0)
        {
            // The supplements: more codes for glyphs given by name
            var count = FontBinary.UInt8(table, position++);
            for (var i = 0; i < count; i++)
            {
                var code = FontBinary.UInt8(table, position);
                var sid = FontBinary.UInt16(table, position + 1);
                position += 3;
                if (GetString(table, strings, sid) is { } name)
                {
                    _builtinEncoding[code] = _glyphsByName.GetValueOrDefault(name);
                }
            }
        }
    }

    /// <summary>Moves the contours of a glyph by an offset (the accent of a <c>seac</c> glyph).</summary>
    private sealed class OffsetSink(OutlineSink sink, double offsetX, double offsetY) : OutlineSink
    {
        public override void MoveTo(double x, double y) => sink.MoveTo(x + offsetX, y + offsetY);

        public override void LineTo(double x, double y) => sink.LineTo(x + offsetX, y + offsetY);

        public override void QuadraticTo(double controlX, double controlY, double x, double y) => sink.QuadraticTo(controlX + offsetX, controlY + offsetY, x + offsetX, y + offsetY);

        public override void CubicTo(double control1X, double control1Y, double control2X, double control2Y, double x, double y) => sink.CubicTo(control1X + offsetX, control1Y + offsetY, control2X + offsetX, control2Y + offsetY, x + offsetX, y + offsetY);

        public override void Close() => sink.Close();
    }
}
