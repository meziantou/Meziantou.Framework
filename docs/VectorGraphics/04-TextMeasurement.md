---
title: Text measurement
description: Measure texts with the Helvetica metrics or a font file, and wrap or truncate them to fit a width.
---

A layout measures its texts to place them: the margins of a chart, the size of the box of a diagram node. An
[ITextMeasurer](cref:Meziantou.Framework.VectorGraphics.ITextMeasurer) measures a line of text in a
[SceneFont](cref:Meziantou.Framework.VectorGraphics.SceneFont), and gives the CSS `font-family` of the SVG.

## Measurers

- [AfmTextMeasurer](cref:Meziantou.Framework.VectorGraphics.AfmTextMeasurer) measures with the metrics of Helvetica,
  Helvetica-Bold, Helvetica-Oblique and Helvetica-BoldOblique, which `Meziantou.Framework.Fonts` embeds. It needs no
  font file and gives the same result on every machine. The SVG asks for `Helvetica, Arial, sans-serif`, which have
  the same widths. A character the font does not have counts as 1 em when it is wide (CJK ideographs, most emoji), as
  nothing when it is a combining mark, and as the average width of the font otherwise.
- [FontFaceTextMeasurer](cref:Meziantou.Framework.VectorGraphics.FontFaceTextMeasurer) shapes the texts with a font of
  `Meziantou.Framework.Fonts` (TrueType, OpenType, WOFF or WOFF2): ligatures, kerning and every script are taken into
  account. Use it when the SVG uses another font; the browser must have that font. Created from a
  [SceneFontCollection](cref:Meziantou.Framework.VectorGraphics.SceneFontCollection), it measures each text with the
  family it names, and the renderers draw the texts with the same fonts. See [fonts of a scene](05-Fonts.md).
- [ImageTextMeasurer](cref:Meziantou.Framework.Imaging.VectorGraphics.ImageTextMeasurer) measures with the fonts that
  draw an image. See [image rendering](03-ImageRendering.md).

## Wrap and truncate texts

[TextFitting](cref:Meziantou.Framework.VectorGraphics.TextFitting) fits a text in a width:

<!-- ref:../../tests/Meziantou.Framework.VectorGraphics.Tests/TextMeasurerTests.cs;region=VectorGraphics_Wrap;format=md-fenced-code-block;dedent=true -->
```csharp
var measurer = AfmTextMeasurer.Default;
var font = new SceneFont(12);

// At most 100 pixels per line, broken between words, and at the line feeds of the text
var lines = TextFitting.Wrap(measurer, "A label that is too long for its box\nSecond paragraph", font, maxWidth: 100);
Assert.Equal(["A label that is too", "long for its box", "Second paragraph"], lines);

// Or shortened with an ellipsis
Assert.Equal("A label that i…", TextFitting.Truncate(measurer, "A label that is too long", font, maxWidth: 80));
```
<!-- endref -->

[Wrap](cref:Meziantou.Framework.VectorGraphics.TextFitting.Wrap) breaks the text at the line break opportunities of
the Unicode line breaking algorithm and at its line feeds, and keeps as many words as fit on each line. A word wider
than the line is broken between two characters, never inside a grapheme cluster such as an emoji or a letter with its
accent. [Truncate](cref:Meziantou.Framework.VectorGraphics.TextFitting.Truncate) shortens a line and appends an ellipsis.
