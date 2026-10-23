using Meziantou.Framework.Fonts;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Pdf.Rendering.Fonts;

/// <summary>
/// A CFF font program (Adobe Technical Note #5176) embedded as <c>/FontFile3</c> (<c>Type1C</c>, <c>CIDFontType0C</c>):
/// the outlines are decoded by the CFF reader of Meziantou.Framework.Fonts; this class reads what a PDF font needs from
/// the Top DICT: the names of the glyphs (charset), the built-in encoding, the CIDs, the font matrix.
/// </summary>
internal sealed class CffFontProgram
{
    private const int CharsetOperator = 15;
    private const int EncodingOperator = 16;
    private const int CharStringsOperator = 17;
    private const int RosOperator = CffDictReader.Escape + 30;
    private const int FontMatrixOperator = CffDictReader.Escape + 7;
    private const int FdArrayOperator = CffDictReader.Escape + 36;

    private readonly byte[] _table;
    private readonly CffTable _cff;
    private readonly Dictionary<string, int> _glyphsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<int, int> _glyphsByCid = [];
    private readonly int[] _builtinEncoding = new int[256];
    private readonly RenderCache _cache;

    public CffFontProgram(byte[] table, RenderCache cache)
    {
        _table = table;
        _cache = cache;
        _cff = CffTable.Read(table);
        var headerSize = FontBinary.UInt8(table, 2);
        var names = CffIndex.Read(table, headerSize);
        var topDicts = CffIndex.Read(table, names.End);
        var strings = CffIndex.Read(table, topDicts.End);

        long charsetOffset = 0;
        long encodingOffset = 0;
        long charStringsOffset = -1;
        long fdArrayOffset = 0;
        PdfMatrix? topMatrix = null;
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
                case CharStringsOperator when count >= 1:
                    charStringsOffset = (long)operands[0];
                    break;
                case RosOperator:
                    IsCidKeyed = true;
                    break;
                case FontMatrixOperator when count >= 6:
                    topMatrix = new PdfMatrix(operands[0], operands[1], operands[2], operands[3], operands[4], operands[5]);
                    break;
                case FdArrayOperator when count >= 1:
                    fdArrayOffset = (long)operands[0];
                    break;
            }
        }

        // The matrix of a CID-keyed font is the one of its font dictionary, then the one of the Top DICT (9): the
        // fonts put the scale in one of them
        var fdMatrix = IsCidKeyed && fdArrayOffset > 0 ? ReadFontDictMatrix(table, fdArrayOffset) : null;
        FontMatrix = (fdMatrix, topMatrix) switch
        {
            ({ } fd, { } top) => fd * top,
            ({ } fd, null) => fd,
            (null, { } top) => top,
            _ => new PdfMatrix(0.001, 0, 0, 0.001, 0, 0),
        };

        GlyphCount = _cff.GlyphCount;

        // The charset: the name (or the CID) of each glyph after .notdef (13)
        var identifiers = ReadCharset(table, charsetOffset, GlyphCount);
        for (var glyph = 0; glyph < identifiers.Length; glyph++)
        {
            if (IsCidKeyed)
            {
                _glyphsByCid.TryAdd(identifiers[glyph], glyph);
            }
            else
            {
                var name = GetString(table, strings, identifiers[glyph]);
                if (name is not null)
                {
                    _glyphsByName.TryAdd(name, glyph);
                }
            }
        }

        if (!IsCidKeyed)
        {
            ReadEncoding(table, encodingOffset, strings);
        }
    }

    public int GlyphCount { get; }

    public bool IsCidKeyed { get; }

    /// <summary>Gets the matrix from the units of the charstrings to text space.</summary>
    public PdfMatrix FontMatrix { get; }

    public int GetGlyphByName(string name) => _glyphsByName.GetValueOrDefault(name);

    /// <summary>Gets the glyph of a CID: through the charset of a CID-keyed font, the CID itself otherwise (9.7.4.2).</summary>
    public int GetGlyphByCid(int cid) => IsCidKeyed ? _glyphsByCid.GetValueOrDefault(cid) : cid;

    /// <summary>Gets the glyph of a code in the encoding of the font program.</summary>
    public int GetGlyphByBuiltinCode(int code) => code is >= 0 and < 256 ? _builtinEncoding[code] : 0;

    public VectorPath? GetPath(int glyph)
    {
        if ((uint)glyph >= (uint)GlyphCount)
            return null;

        var sink = new GlyphPathSink();
        var budget = new GlyphBudget(_cache.FontEnvironment.Limits);
        if (_cff.DecodeWithAccentedGlyphs(_table, glyph, sink, ref budget) is { } accented && !IsCidKeyed)
        {
            // A glyph made of a base character and an accent, by their names in the standard encoding; the components
            // cannot be accented glyphs themselves
            if (Pdf.Fonts.FontEncodings.Standard[accented.BaseCode & 0xFF] is { } baseName && _glyphsByName.TryGetValue(baseName, out var baseGlyph))
            {
                _cff.Decode(_table, baseGlyph, sink, ref budget);
            }

            if (Pdf.Fonts.FontEncodings.Standard[accented.AccentCode & 0xFF] is { } accentName && _glyphsByName.TryGetValue(accentName, out var accentGlyph))
            {
                sink.Close();
                sink.OffsetX = accented.AccentX;
                sink.OffsetY = accented.AccentY;
                _cff.Decode(_table, accentGlyph, sink, ref budget);
            }
        }

        return sink.ToPath();
    }

    /// <summary>Reads the font matrix of the first font dictionary of a CID-keyed font; <see langword="null"/> when it has none.</summary>
    private static PdfMatrix? ReadFontDictMatrix(byte[] table, long offset)
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
                return new PdfMatrix(operands[0], operands[1], operands[2], operands[3], operands[4], operands[5]);
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

    /// <summary>Reads the charset (13): the identifier of each glyph, 0 for .notdef.</summary>
    private static int[] ReadCharset(byte[] table, long offset, int glyphCount)
    {
        var result = new int[glyphCount];
        if (offset is 0 or 1 or 2)
        {
            // The predefined charsets: ISOAdobe has the identifiers of the glyphs in order; the expert charsets are not used by PDF fonts
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

    /// <summary>Reads the encoding (12): the code of each glyph, and the supplements.</summary>
    private void ReadEncoding(byte[] table, long offset, CffIndex strings)
    {
        if (offset is 0 or 1)
        {
            // The standard encoding: the glyphs of the names of the codes
            for (var code = 0; code < 256; code++)
            {
                if (Pdf.Fonts.FontEncodings.Standard[code] is { } name)
                {
                    _builtinEncoding[code] = GetGlyphByName(name);
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
                    _builtinEncoding[code] = GetGlyphByName(name);
                }
            }
        }

    }
}
