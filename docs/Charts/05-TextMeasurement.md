---
title: Text measurement
description: How the layout measures texts, the default Helvetica metrics, and measuring with a font file.
---

The layout measures every text: the width of the tick labels gives the margins, decides whether the labels of the
horizontal axis are rotated, and whether a label fits in its slice, its circle or its rectangle. An
[ITextMeasurer](cref:Meziantou.Framework.Charts.ITextMeasurer) measures them, and gives the CSS `font-family` of the SVG.

## The default: Helvetica metrics

[AfmTextMeasurer](cref:Meziantou.Framework.Charts.AfmTextMeasurer) measures texts with the metrics of Helvetica and
Helvetica-Bold, which `Meziantou.Framework.Fonts` embeds. It needs no font file and gives the same result on every
machine. It applies the kerning of the font, and measures by grapheme cluster: a character the font does not have counts
as 1 em when it is wide (CJK ideographs, most emoji), as nothing when it is a combining mark, and as the average width of
the font otherwise. The SVG asks for `Helvetica, Arial, sans-serif`: Arial and Liberation Sans have the same widths, so
the texts fit when the browser draws them.

## Measure with a font file

When the texts are in other scripts, or the SVG uses another font, measure with that font.
[FontFaceTextMeasurer](cref:Meziantou.Framework.Charts.FontFaceTextMeasurer) shapes the texts with a font of
`Meziantou.Framework.Fonts` (TrueType, OpenType, WOFF or WOFF2), so ligatures, kerning and every script are taken into
account:

<!-- ref:../../tests/Meziantou.Framework.Charts.Tests/TextMeasurerTests.cs;region=Charts_FontFaceTextMeasurer;format=md-fenced-code-block;dedent=true -->
```csharp
var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "LiberationSans-Regular.woff2"));
var face = FontLoader.Load(bytes, new PooledFontEnvironment(FontResourceLimits.Default, 64 * 1024 * 1024))[0];
var measurer = new FontFaceTextMeasurer(face, fontFamily: "'Liberation Sans', Arial, sans-serif");

var plot = new Plot { Title = "Measured with Liberation Sans", TextMeasurer = measurer };
plot.Add(new BarMark<Sale>(SampleData.Sales.Where(sale => sale.Year == 2024), x: sale => sale.Region, y: sale => sale.Amount));
var svg = plot.ToSvg();
```
<!-- endref -->

[Plot.TextMeasurer](cref:Meziantou.Framework.Charts.Plot.TextMeasurer) sets the measurer of a plot;
[CreateScene](cref:Meziantou.Framework.Charts.Plot.CreateScene) also takes one. The SVG declares the family of the measurer,
unless the theme sets [FontFamily](cref:Meziantou.Framework.Charts.ChartTheme.FontFamily). The SVG does not embed the
font: the browser must have it, for instance through a CSS `@font-face` rule of the page.
