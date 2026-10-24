# Meziantou.Framework.Fonts

The fully managed OpenType font reader and text shaper of [`Meziantou.Framework.Imaging`](https://www.nuget.org/packages/Meziantou.Framework.Imaging) and [`Meziantou.Framework.WebPlatform`](https://www.nuget.org/packages/Meziantou.Framework.WebPlatform).

Its public API (`FontLoader`, `FontFace`, `TextShaper`, `GlyphBuffer`, ...) is the one these packages use: to load fonts and draw text, `Meziantou.Framework.Imaging` is the simpler entry point. The packages that use it depend on its exact version, so update them together.

What it covers:

- TrueType and OpenType fonts and font collections, WOFF 1.0 and WOFF 2.0
- TrueType, CFF and CFF2 outlines, variable fonts (`fvar`, `avar` 1 and 2, `gvar`, `HVAR`, `VVAR`, `MVAR`)
- Color fonts: `COLR` versions 0 and 1, `CPAL`, `sbix`, `CBDT`
- Horizontal and vertical metrics, `BASE` baselines
- Text shaping with `GSUB` and `GPOS`, and the shaping models of complex scripts (Arabic, Hebrew, Indic, Khmer, Myanmar, Thai and Lao, Hangul, and the universal shaping engine)
- The fonts installed on Windows, macOS and Linux (`SystemFontCollection`): an index of their faces by family and by name, read without loading the fonts, and the font matching of CSS to choose a face of a family
- AFM files (Adobe Font Metrics, `AfmFontMetrics`): the metrics of a PostScript font without its glyphs, such as the 14 standard fonts of PDF: global metrics, widths, bounding boxes and ligatures of the characters, kerning pairs and tracks, composite characters

The types are in the `Meziantou.Framework.Fonts` namespace:

<!-- ref:../../samples/Meziantou.Framework.Fonts.Samples/Program.cs;region=Fonts_SystemFontCollection;format=md-fenced-code-block;dedent=true -->
```csharp
// The fonts of the machine are scanned once per process; only the tables that describe a face are read.
var fonts = SystemFontCollection.Default;
if (fonts.Match("Arial", weight: 700, isItalic: false) is { } face)
{
    using var stream = File.OpenRead(face.FilePath);
    var font = FontLoader.Load(stream, new PooledFontEnvironment(FontResourceLimits.Default, 64 * 1024 * 1024))[face.FaceIndex];
    Console.WriteLine($"{font.FamilyName} {font.SubfamilyName}: {font.UnitsPerEm} units per em");
}
```
<!-- endref -->

An AFM file gives the metrics of text without loading a font:

<!-- ref:../../tests/Meziantou.Framework.Imaging.Tests/FontTests.cs;region=AfmFontMetrics_Load;format=md-fenced-code-block;dedent=true -->
```csharp
// An AFM file gives the metrics of a PostScript font without its glyphs, here two characters of Helvetica.
var metrics = AfmFontMetrics.Parse("""
    StartFontMetrics 4.1
    FontName Helvetica
    StartCharMetrics 2
    C 65 ; WX 667 ; N A ; B 14 0 654 718 ;
    C 86 ; WX 667 ; N V ; B 20 0 647 718 ;
    EndCharMetrics
    StartKernData
    StartKernPairs 1
    KPX A V -70
    EndKernPairs
    EndKernData
    EndFontMetrics
    """);

// The metrics are in 1/1000 of the point size: the width of "AV" at 12 points, with its kerning.
var units = metrics.GetWidth("A")!.Value.X + metrics.GetKerning("A", "V").X + metrics.GetWidth("V")!.Value.X;
var width = units * 12 / 1000;
Assert.Equal(15.168, width);
```
<!-- endref -->

The multiple master (AMFM) and composite (ACFM) metrics files are not supported.

The installed fonts are found in the fonts directory of Windows, in the fonts installed for the user and in the files the registry lists; in the `Library/Fonts` directories of macOS and in the fonts it downloads on demand; and on Linux in the directories of the configuration of fontconfig (`fonts.conf` and the files it includes) and in the XDG font directories. `SystemFontCollection.Create` indexes chosen directories instead.

It is written from the specifications and checked against HarfBuzz and FreeType.
