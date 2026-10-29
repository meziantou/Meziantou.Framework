---
title: Installed fonts
description: Find the fonts installed on Windows, macOS and Linux, or in directories you choose, choose a face of a family, and find a font that has a character.
---

## Find an installed font

`SystemFontCollection.Default` is an index of the fonts installed on the machine. `Match` finds the face of a family
that is the closest to a weight and an italic, and the descriptor gives the file to load:

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

- The fonts are scanned the first time `Default` is read, once per process. Only the tables that describe a face are
  read, not the glyphs. A font installed later is not seen.
- `Match` uses the font matching of CSS: the normal width first, then the style, then the weight. The weight is from 1
  to 1000 (400 is normal, 700 bold); an italic face is preferred when `isItalic` is set, then an oblique face, then an
  upright one. It returns `null` when no font has the family: another family is never used instead.
- A variable font matches the weights and the widths of its axes; choosing the instance is up to you (see
  [Variable fonts](05-VariableFonts.md)).
- `GetFamily` gives every face of a family, and `FindByName` finds a face by its full name or its PostScript name, as
  the `local()` function of CSS does. Names are compared without case, in every language of the fonts.
- The file of a descriptor may have changed or been removed since the scan: loading it can fail.

The installed fonts are found in the fonts directory of Windows, in the fonts installed for the user and in the files
the registry lists; in the `Library/Fonts` directories of macOS and in the fonts it downloads on demand; and on Linux in
the directories of the fontconfig configuration and in the XDG font directories.

## Find a font that has a character

`SystemFontDescriptor.Covers` tells whether a face has a glyph for a character, to choose a fallback font:

<!-- ref:../../samples/Meziantou.Framework.Fonts.Samples/Program.cs;region=Fonts_Coverage;format=md-fenced-code-block;dedent=true -->
```csharp
// The characters of a face are read from its file the first time they are asked for, then kept with the collection.
var grinningFace = new Rune(0x1F600);
var emojiFont = SystemFontCollection.Default.Fonts.FirstOrDefault(face => face.Covers(grinningFace));
Console.WriteLine($"U+1F600: {emojiFont?.FamilyName ?? "no installed font"}");
```
<!-- endref -->

The characters of a face are read from its file the first time `Covers` asks for one, and kept with the collection
within a bounded amount of memory: scanning the fonts reads none of them.

## Use the fonts of a directory

`SystemFontCollection.Create` indexes the fonts of directories (and their subdirectories) and of font files, such as the
fonts your application ships:

<!-- ref:../../tests/Meziantou.Framework.Imaging.Tests/FontTests.cs;region=Fonts_FontDirectory;format=md-fenced-code-block;dedent=true -->
```csharp
static string? FindFont(string directory, string familyName)
{
    // The fonts of a directory and of its subdirectories: only the tables that describe a face are read
    var fonts = SystemFontCollection.Create([directory]);
    var face = fonts.Match(familyName, weight: 700); // the closest face of the family, as CSS chooses it
    return face is null ? null : string.Create(CultureInfo.InvariantCulture, $"{face.FamilyName} {face.SubfamilyName}, weight {face.Weight}, face {face.FaceIndex} of {Path.GetFileName(face.FilePath)}");
}
```
<!-- endref -->

With a directory that holds the regular face of Noto Sans, `FindFont(directory, "noto sans")` gives
`Noto Sans Regular, weight 400, face 0 of NotoSans-Regular.subset.ttf`: the family has no bold face, so the closest one
is used.

- The `.ttf`, `.otf`, `.ttc`, `.otc`, `.woff` and `.woff2` files are indexed; the other files are ignored, and so is a
  path that does not exist.
- A collection is immutable and can be used by several threads at the same time.

The fonts of a machine are not those of another one: a text drawn with an installed font can look different elsewhere.
To get the same result everywhere, ship the font files with your application.
