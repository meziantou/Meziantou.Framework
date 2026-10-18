using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The painting of the color glyphs (https://learn.microsoft.com/typography/opentype/spec/colr): a glyph made of colored
// layers is replaced in its glyph run with the glyphs of its layers, each one painted with the color of its entry in the
// palette selected by font-palette (https://drafts.csswg.org/css-fonts-4/#font-palette-prop). The layers are ordinary glyph
// runs of the display list: a rendering backend does not know the color glyphs.
internal sealed partial class Painter
{
    /// <summary>The palettes of the fonts that have color glyphs, by style (the text fragments of an element share its style).</summary>
    private Dictionary<(ComputedStyle Style, MatchedFont Font), FontPalette>? _fontPalettes;

    /// <summary>Returns the palette of the color glyphs of a font for a style, or null when the font has no color glyph.</summary>
    private FontPalette? GetFontPalette(ComputedStyle style, MatchedFont font)
    {
        if (font.Font.ColorGlyphs is null)
            return null;

        _fontPalettes ??= [];
        if (!_fontPalettes.TryGetValue((style, font), out var palette))
        {
            palette = FontPalette.Resolve(style, font)!;
            _fontPalettes.Add((style, font), palette);
        }

        return palette;
    }

    /// <summary>
    /// Adds a glyph run to the display list. The color glyphs of the run are drawn as their layers
    /// (<see cref="FontPalette.SplitColorGlyphs"/>) when <paramref name="palette"/> is the palette of the font of the run:
    /// with the colors of the palette when <paramref name="colorize"/> (the fill of the text), else with the paint (a shadow,
    /// a mask); a stroke does not draw them.
    /// </summary>
    private void AddGlyphRun(GlyphRun run, FontPalette? palette, DisplayPaint paint, bool colorize, Dom.Node? source)
    {
        if (palette is null || !palette.HasColorGlyph(run))
        {
            _builder.Add(new DrawGlyphsItem(run, paint) { Source = source });
            return;
        }

        foreach (var (layerRun, layerPaint) in palette.SplitColorGlyphs(run, paint, colorize))
        {
            _builder.Add(new DrawGlyphsItem(layerRun, layerPaint) { Source = source });
        }
    }
}
