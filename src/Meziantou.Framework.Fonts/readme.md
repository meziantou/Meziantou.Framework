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

The installed fonts are found in the fonts directory of Windows, in the fonts installed for the user and in the files the registry lists; in the `Library/Fonts` directories of macOS and in the fonts it downloads on demand; and on Linux in the directories of the configuration of fontconfig (`fonts.conf` and the files it includes) and in the XDG font directories. `SystemFontCollection.Create` indexes chosen directories instead.

It is written from the specifications and checked against HarfBuzz and FreeType.
