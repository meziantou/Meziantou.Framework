using Meziantou.Framework.Fonts;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Pdf.Rendering.Fonts;

/// <summary>
/// A CFF font program (Adobe Technical Note #5176) embedded as <c>/FontFile3</c> (<c>Type1C</c>, <c>CIDFontType0C</c>):
/// the names of the glyphs (charset), the built-in encoding, the CIDs, the font matrix and the outlines are read by
/// <see cref="CffFont"/> of Meziantou.Framework.Fonts.
/// </summary>
internal sealed class CffFontProgram
{
    private readonly CffFont _font;
    private readonly RenderCache _cache;

    public CffFontProgram(byte[] table, RenderCache cache)
    {
        _font = CffFont.Load(table);
        _cache = cache;
        var matrix = _font.FontMatrix;
        FontMatrix = new PdfMatrix(matrix[0], matrix[1], matrix[2], matrix[3], matrix[4], matrix[5]);
    }

    public int GlyphCount => _font.GlyphCount;

    public bool IsCidKeyed => _font.IsCidKeyed;

    /// <summary>Gets the matrix from the units of the charstrings to text space.</summary>
    public PdfMatrix FontMatrix { get; }

    public int GetGlyphByName(string name) => _font.GetGlyphByName(name);

    /// <summary>Gets the glyph of a CID: through the charset of a CID-keyed font, the CID itself otherwise (9.7.4.2).</summary>
    public int GetGlyphByCid(int cid) => _font.GetGlyphByCid(cid);

    /// <summary>Gets the glyph of a code in the encoding of the font program.</summary>
    public int GetGlyphByBuiltinCode(int code) => _font.GetGlyphByCode(code);

    public VectorPath? GetPath(int glyph)
    {
        if ((uint)glyph >= (uint)GlyphCount)
            return null;

        var sink = new GlyphPathSink();
        var budget = new GlyphBudget(_cache.FontEnvironment.Limits);
        _font.DecodeOutline(glyph, sink, ref budget);
        return sink.ToPath();
    }
}
