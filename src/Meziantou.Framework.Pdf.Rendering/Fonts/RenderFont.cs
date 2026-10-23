using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Text;

namespace Meziantou.Framework.Pdf.Rendering.Fonts;

/// <summary>
/// A font of a document, ready to be drawn: the codes of its strings, their widths (by <see cref="PdfFontDecoder"/>),
/// and the outline of the glyph of each code in text space (one unit is the font size).
/// </summary>
internal class RenderFont
{
    // The outlines a font keeps: the glyphs of a page are drawn many times
    private const int MaxCachedGlyphs = 4096;

    private readonly Dictionary<uint, VectorPath?> _paths = [];
    private readonly Func<uint, VectorPath?> _getGlyph;

    public RenderFont(PdfFontDecoder decoder, PdfMatrix fontMatrix, Func<uint, VectorPath?> getGlyph)
    {
        Decoder = decoder;
        FontMatrix = fontMatrix;
        _getGlyph = getGlyph;
    }

    public PdfFontDecoder Decoder { get; }

    /// <summary>Gets the matrix from the units of the glyphs to text space.</summary>
    public PdfMatrix FontMatrix { get; }

    public bool IsVertical => Decoder.IsVertical;

    /// <summary>Gets the glyph procedures of a Type 3 font, or <see langword="null"/>.</summary>
    public virtual Type3Glyphs? Type3 => null;

    /// <summary>Gets the outline of the glyph of a code, in text space; <see langword="null"/> for a glyph without outline.</summary>
    public VectorPath? GetGlyphPath(uint code)
    {
        if (_paths.TryGetValue(code, out var path))
            return path;

        try
        {
            path = _getGlyph(code);
            if (path is not null && !FontMatrix.Equals(PdfMatrix.Identity))
            {
                path = path.Transform(new AffineTransform(FontMatrix.A, FontMatrix.B, FontMatrix.C, FontMatrix.D, FontMatrix.E, FontMatrix.F));
            }
        }
        catch (Exception ex) when (RenderCache.IsContentError(ex) || ex is ArgumentException)
        {
            path = null;
        }

        if (_paths.Count < MaxCachedGlyphs)
        {
            _paths[code] = path;
        }

        return path;
    }
}
