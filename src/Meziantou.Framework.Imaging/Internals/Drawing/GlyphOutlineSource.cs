using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Where the outline of a glyph comes from: decoded from its font, or replayed from what a layout recorded when it was
/// created (the hinted outline of a hinted layout).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct GlyphOutlineSource
{
    private readonly TextLayout? _layout;
    private readonly int _index;

    private GlyphOutlineSource(FontFace face, int glyph, double size, TextLayout? layout, int index, TextHinting hinting)
    {
        Face = face;
        Glyph = glyph;
        Size = size;
        _layout = layout;
        _index = index;
        Hinting = hinting;
    }

    public FontFace Face { get; }

    public int Glyph { get; }

    /// <summary>Gets the size the outline is decoded at, in pixels per em.</summary>
    public double Size { get; }

    /// <summary>Gets how the outline was fitted to the pixel grid: the outlines of a glyph differ from one hinting to another.</summary>
    public TextHinting Hinting { get; }

    /// <summary>Gets the outline of a glyph of a font.</summary>
    public static GlyphOutlineSource FromFont(FontFace face, int glyph, double size) => new(face, glyph, size, layout: null, index: 0, TextHinting.None);

    /// <summary>Gets the outline a layout recorded for one of its glyphs.</summary>
    public static GlyphOutlineSource FromLayout(TextLayout layout, int index, FontFace face, double size) => new(face, layout.GetGlyph(index), size, layout, index, layout.Hinting);

    /// <summary>Sends the outline, in font units, to a sink.</summary>
    public void Send(OutlineSink sink, GlyphScratch scratch)
    {
        if (_layout is not null)
        {
            _layout.ReplayOutline(_index, sink);
            return;
        }

        var budget = new GlyphBudget(Face.Data.Limits);
        Face.DecodeOutline(Glyph, Size, sink, scratch, ref budget);
    }
}
