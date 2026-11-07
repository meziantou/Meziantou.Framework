---
title: Glyphs
description: Map characters to glyphs, read the metrics of a font and of its glyphs, decode outlines, and find color glyphs.
---

## Map characters to glyphs

A font draws glyphs, not characters. [MapCodePoint](cref:Meziantou.Framework.Fonts.FontFace.MapCodePoint) gives the glyph of a character from the character map of the font,
or 0, the missing glyph. [MapVariation](cref:Meziantou.Framework.Fonts.FontFace.MapVariation) gives the glyph of a variation sequence, a character followed by a variation
selector.

Mapping characters one by one is enough for simple scripts and monospaced text. For most text, shape it instead: the
font can replace, merge and move glyphs, which the character map does not say. See [Shaping](06-Shaping.md).

## Read the metrics

The metrics are in font units: divide them by [UnitsPerEm](cref:Meziantou.Framework.Fonts.FontFace.UnitsPerEm) and multiply them by the size of the text.

| Metrics | Members of [FontFace](cref:Meziantou.Framework.Fonts.FontFace) |
| --- | --- |
| Lines | [HorizontalAscender](cref:Meziantou.Framework.Fonts.FontFace.HorizontalAscender), [HorizontalDescender](cref:Meziantou.Framework.Fonts.FontFace.HorizontalDescender), [HorizontalLineGap](cref:Meziantou.Framework.Fonts.FontFace.HorizontalLineGap), [TypoAscender](cref:Meziantou.Framework.Fonts.FontFace.TypoAscender), [TypoDescender](cref:Meziantou.Framework.Fonts.FontFace.TypoDescender), [TypoLineGap](cref:Meziantou.Framework.Fonts.FontFace.TypoLineGap), [UseTypoMetrics](cref:Meziantou.Framework.Fonts.FontFace.UseTypoMetrics), [WinAscent](cref:Meziantou.Framework.Fonts.FontFace.WinAscent), [WinDescent](cref:Meziantou.Framework.Fonts.FontFace.WinDescent) |
| Letters | [CapHeight](cref:Meziantou.Framework.Fonts.FontFace.CapHeight), [XHeight](cref:Meziantou.Framework.Fonts.FontFace.XHeight), [AverageCharWidth](cref:Meziantou.Framework.Fonts.FontFace.AverageCharWidth) |
| Decorations | [UnderlinePosition](cref:Meziantou.Framework.Fonts.FontFace.UnderlinePosition), [UnderlineThickness](cref:Meziantou.Framework.Fonts.FontFace.UnderlineThickness), [StrikeoutPosition](cref:Meziantou.Framework.Fonts.FontFace.StrikeoutPosition), [StrikeoutSize](cref:Meziantou.Framework.Fonts.FontFace.StrikeoutSize), [SubscriptYOffset](cref:Meziantou.Framework.Fonts.FontFace.SubscriptYOffset), [SuperscriptYOffset](cref:Meziantou.Framework.Fonts.FontFace.SuperscriptYOffset) |
| Style | [Weight](cref:Meziantou.Framework.Fonts.FontFace.Weight), [WidthClass](cref:Meziantou.Framework.Fonts.FontFace.WidthClass), [IsItalic](cref:Meziantou.Framework.Fonts.FontFace.IsItalic), [ItalicAngle](cref:Meziantou.Framework.Fonts.FontFace.ItalicAngle), [IsMonospaced](cref:Meziantou.Framework.Fonts.FontFace.IsMonospaced) |
| Glyphs | [GetAdvanceWidth](cref:Meziantou.Framework.Fonts.FontFace.GetAdvanceWidth), [GetLeftSideBearing](cref:Meziantou.Framework.Fonts.FontFace.GetLeftSideBearing), [GetAdvanceHeight](cref:Meziantou.Framework.Fonts.FontFace.GetAdvanceHeight), [GetTopSideBearing](cref:Meziantou.Framework.Fonts.FontFace.GetTopSideBearing), [GetVerticalOrigin](cref:Meziantou.Framework.Fonts.FontFace.GetVerticalOrigin), [TryGetGlyphBounds](cref:Meziantou.Framework.Fonts.FontFace.TryGetGlyphBounds) |
| Vertical text | [HasVerticalMetrics](cref:Meziantou.Framework.Fonts.FontFace.HasVerticalMetrics), [VerticalAscender](cref:Meziantou.Framework.Fonts.FontFace.VerticalAscender), [VerticalDescender](cref:Meziantou.Framework.Fonts.FontFace.VerticalDescender), [VerticalLineGap](cref:Meziantou.Framework.Fonts.FontFace.VerticalLineGap) |
| Baselines | [TryGetBaseline](cref:Meziantou.Framework.Fonts.FontFace.TryGetBaseline), [TryGetIdeographicCenter](cref:Meziantou.Framework.Fonts.FontFace.TryGetIdeographicCenter) |
| Names | [FamilyName](cref:Meziantou.Framework.Fonts.FontFace.FamilyName), [SubfamilyName](cref:Meziantou.Framework.Fonts.FontFace.SubfamilyName), [PostScriptName](cref:Meziantou.Framework.Fonts.FontFace.PostScriptName), [GetName](cref:Meziantou.Framework.Fonts.FontFace.GetName), [GetNames](cref:Meziantou.Framework.Fonts.FontFace.GetNames) |

[TableTags](cref:Meziantou.Framework.Fonts.FontFace.TableTags) lists the tables of the font, and [TryGetTable](cref:Meziantou.Framework.Fonts.FontFace.TryGetTable) gives the bytes of any table, to read what the API does not
expose.

## Decode an outline

[DecodeOutline](cref:Meziantou.Framework.Fonts.FontFace.DecodeOutline) sends the outline of a glyph to an [OutlineSink](cref:Meziantou.Framework.Fonts.OutlineSink): a sequence of contours made of lines and quadratic or
cubic curves, in font units, with the y axis going up. Write a sink for your renderer. This one writes the data of an SVG
path:

<!-- ref:../../tests/Meziantou.Framework.Imaging.Tests/FontTests.cs;region=Fonts_SvgPathSink;format=md-fenced-code-block;dedent=true -->
```csharp
// Writes an outline as the data of an SVG path, in font units. The y axis of a font goes up, the one of SVG down.
private sealed class SvgPathSink : OutlineSink
{
    private readonly StringBuilder _path = new();

    public override void MoveTo(double x, double y) => Append('M', x, y);

    public override void LineTo(double x, double y) => Append('L', x, y);

    public override void QuadraticTo(double controlX, double controlY, double x, double y) => Append('Q', controlX, controlY, x, y);

    public override void CubicTo(double control1X, double control1Y, double control2X, double control2Y, double x, double y) => Append('C', control1X, control1Y, control2X, control2Y, x, y);

    public override void Close() => _path.Append('Z');

    public override string ToString() => _path.ToString();

    private void Append(char command, params ReadOnlySpan<double> coordinates)
    {
        _path.Append(command);
        for (var i = 0; i < coordinates.Length; i += 2)
        {
            _path.Append(CultureInfo.InvariantCulture, $"{(i > 0 ? " " : "")}{coordinates[i]} {0 - coordinates[i + 1]}");
        }
    }
}
```
<!-- endref -->

<!-- ref:../../tests/Meziantou.Framework.Imaging.Tests/FontTests.cs;region=Fonts_DecodeOutline;format=md-fenced-code-block;dedent=true -->
```csharp
static (string Path, GlyphBox Bounds) GetOutline(FontFace face, char character)
{
    var glyph = face.MapCodePoint(character);

    // The working storage of the outline, rented from the environment of the font, and the budget of the glyph
    using var scratch = new GlyphScratch(face.Data.Environment.CreateAllocator("outline"));
    var budget = new GlyphBudget(face.Data.Limits);

    var sink = new SvgPathSink();
    face.DecodeOutline(glyph, sink, scratch, ref budget);

    face.TryGetGlyphBounds(glyph, scratch, out var bounds); // false for a glyph without outline, such as a space
    return (sink.ToString(), bounds);
}
```
<!-- endref -->

For the letter `l` of Noto Sans, the path is `M173 0L85 0L85 -760L173 -760L173 0Z`, and the bounds go from (85, 0) to
(173, 760).

- A [GlyphScratch](cref:Meziantou.Framework.Fonts.GlyphScratch) is the working storage of the outlines, rented from an allocator of the environment. Keep one per
  thread and reuse it: decoding the same glyphs again allocates nothing. Dispose it to return its storage.
- A [GlyphBudget](cref:Meziantou.Framework.Fonts.GlyphBudget) counts the work of one glyph against [MaxGlyphComplexity](cref:Meziantou.Framework.Fonts.FontResourceLimits.MaxGlyphComplexity): create one per glyph.
- A glyph without outline, such as a space, emits nothing.
- [GlyphBoundsSink](cref:Meziantou.Framework.Fonts.GlyphBoundsSink) is a sink that measures an outline: the smallest rectangle that contains its points and the control
  points of its curves.
- The [DecodeOutline](cref:Meziantou.Framework.Fonts.FontFace.DecodeOutline) overload that takes a size uses the bitmaps of the font at that size when the font was loaded with
  `useEmbeddedBitmaps`.

## Hint outlines

At small sizes, an outline scaled exactly puts its edges across pixels. [GlyphHinter](cref:Meziantou.Framework.Fonts.GlyphHinter) decodes outlines fitted to the
pixel grid of a size:

- [GlyphHinting.Light](cref:Meziantou.Framework.Fonts.GlyphHinting.Light) aligns the outlines along the vertical axis, without the instructions of the font, for any
  outline format.
- [GlyphHinting.Full](cref:Meziantou.Framework.Fonts.GlyphHinting.Full) runs the TrueType instructions of the font. Instructions that break a rule of the specification
  leave the glyph, or the font when they are in its programs, unhinted. [Hinting](cref:Meziantou.Framework.Fonts.GlyphHinter.Hinting) gives the mode that is used: [Light](cref:Meziantou.Framework.Fonts.GlyphHinting.Light)
  when the instructions of the font cannot be run.

[GlyphHinter.Create](cref:Meziantou.Framework.Fonts.GlyphHinter.Create) prepares a font at a size, and [DecodeOutline](cref:Meziantou.Framework.Fonts.GlyphHinter.DecodeOutline) gives the hinted outline of a glyph and its advance
in pixels. A hinter keeps the state of the programs of the font: use it for one operation at a time, and dispose it.

## Color glyphs

Some fonts have glyphs with their own colors, such as emoji. [HasColorGlyphs](cref:Meziantou.Framework.Fonts.FontFace.HasColorGlyphs) tells whether a font has some, and
[IsColorGlyph](cref:Meziantou.Framework.Fonts.FontFace.IsColorGlyph) whether a glyph is one.

- [BuildColorGlyph](cref:Meziantou.Framework.Fonts.FontFace.BuildColorGlyph) gives the display list of a glyph of a `COLR` table, with the colors of a palette: layers, paints,
  gradients and compositions to draw. [PaletteCount](cref:Meziantou.Framework.Fonts.FontFace.PaletteCount), [TryGetPaletteColor](cref:Meziantou.Framework.Fonts.FontFace.TryGetPaletteColor) and [GetPaletteType](cref:Meziantou.Framework.Fonts.FontFace.GetPaletteType) read the palettes of the
  `CPAL` table.
- [TryGetBitmapGlyph](cref:Meziantou.Framework.Fonts.FontFace.TryGetBitmapGlyph) finds the image of a glyph of an `sbix` or `CBDT` table in the strike that suits a size: where its
  data is in the font, its placement and its image format ([BitmapGlyph.Format](cref:Meziantou.Framework.Fonts.BitmapGlyph.Format)). Decoding the image is up to you; a
  renderer can also draw the outline of the glyph instead.

To draw color glyphs without writing a renderer, use [the text API of Meziantou.Framework.Imaging](../Imaging/08-Text/02-Fonts.md#color-glyphs).
