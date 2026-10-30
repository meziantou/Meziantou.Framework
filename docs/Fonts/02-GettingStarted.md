---
title: Getting started
description: Install the package, load a font file, and read its names, metrics and glyphs.
---

## Install the package

Add the `Meziantou.Framework.Fonts` package to a project that targets .NET 10 or later:

```bash
dotnet add package Meziantou.Framework.Fonts
```

The examples of this documentation omit the `using` directives: they need `using Meziantou.Framework.Fonts;`, and
`using Meziantou.Framework;` for the Unicode types (`UnicodeScript`).

## Load a font and read it

`FontLoader.Load` reads a font file and gives its faces. A `PooledFontEnvironment` gives the limits of the font code
and the storage of its temporary buffers:

<!-- ref:../../tests/Meziantou.Framework.Imaging.Tests/FontTests.cs;region=Fonts_LoadFontFace;format=md-fenced-code-block;dedent=true -->
```csharp
static string DescribeFont(string path)
{
    // The limits that protect the application from hostile fonts, and the storage of the temporary buffers
    var environment = new PooledFontEnvironment(FontResourceLimits.Default, maxLiveAllocationBytes: 64 * 1024 * 1024);

    // One face per font of the file: a font collection has several
    using var stream = File.OpenRead(path);
    var face = FontLoader.Load(stream, environment)[0];

    var glyph = face.MapCodePoint('A'); // 0 when the font does not have the character
    return string.Create(
        CultureInfo.InvariantCulture,
        $"{face.FamilyName} {face.SubfamilyName}: {face.UnitsPerEm} units per em, 'A' is glyph {glyph}, {face.GetAdvanceWidth(glyph)} units wide");
}
```
<!-- endref -->

With the regular face of Noto Sans, the result is
`Noto Sans Regular: 1000 units per em, 'A' is glyph 34, 639 units wide`.

- A font file gives one face, and a font collection (`.ttc`, `.otc`) one face per font. The format is detected from
  the content, never from the file name; WOFF and WOFF2 files are expanded in memory.
- The values of a font are in font units: divide them by `UnitsPerEm` and multiply them by the size of the text to
  get pixels or points.
- `MapCodePoint` gives the glyph of a character, or 0, the missing glyph, when the font does not have it.
- Load a font once and reuse it: a `FontFace` is immutable and can be used by several threads at the same time.

## Next steps

- [Loading fonts](03-LoadingFonts.md): environments, limits and errors.
- [Glyphs](04-Glyphs.md): metrics, outlines and color glyphs.
- [Shaping](06-Shaping.md): turn a text into positioned glyphs.
