---
title: AFM metrics
description: Read the metrics of PostScript fonts from AFM files, including the embedded metrics of the 14 standard fonts of PDF, to measure text.
---

An AFM file (Adobe Font Metrics) gives the metrics of a PostScript font without its glyphs: the widths and the boxes of
its characters, its kerning pairs and its ligatures. A PDF writer uses them to measure text in the 14 standard fonts of
PDF, which PDF readers provide.

## Measure text with a standard font

The metrics of the 14 standard fonts are embedded: `AfmFontMetrics.GetStandardFont` gives them by name, and
`StandardFontNames` lists the names.

<!-- ref:../../tests/Meziantou.Framework.Imaging.Tests/FontTests.cs;region=AfmFontMetrics_StandardFont;format=md-fenced-code-block;dedent=true -->
```csharp
// The metrics of the 14 standard fonts of PDF are embedded
var helvetica = AfmFontMetrics.GetStandardFont("Helvetica-Bold")!;

// The width of a text at 12 points: the widths are in 1/1000 of the point size
var units = 0.0;
string? previous = null;
foreach (var character in "Total")
{
    var metrics = helvetica.GetCharacterByCode(character)!; // the codes of the standard encoding of the font
    units += helvetica.GetWidth(metrics)!.Value.X;
    if (previous is not null)
        units += helvetica.GetKerning(previous, metrics.Name!).X;

    previous = metrics.Name;
}

Assert.Equal(27.708, units * 12 / 1000);
Assert.HasCount(14, AfmFontMetrics.StandardFontNames);
```
<!-- endref -->

- The metrics are in 1/1000 of the point size.
- `GetStandardFont` returns `null` for another name; the names are case-sensitive (`Helvetica`, not `helvetica`).
- A character is found by its name (`GetCharacter`, `GetWidth`) or by its code in the encoding of the font
  (`GetCharacterByCode`).

## Read an AFM file

`AfmFontMetrics.Parse` reads the text of a file, and `Load` and `LoadAsync` read a stream or bytes:

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

| Members | Gives |
| --- | --- |
| `FontName`, `FullName`, `FamilyName`, `Weight`, `Version`, `Notice` | The names of the font |
| `FontBBox`, `Ascender`, `Descender`, `CapHeight`, `XHeight`, `StdHW`, `StdVW` | The global metrics |
| `CharacterMetrics`, `GetCharacter`, `GetCharacterByCode`, `GetWidth`, `GetVerticalWidth` | The characters: code, name, width, box and ligatures |
| `KerningPairs`, `GetKerning`, `VerticalKerningPairs`, `GetVerticalKerning` | The kerning of pairs |
| `TrackKerning`, `GetTrackKerning` | The kerning that depends on the size |
| `Composites` | The characters made of other characters |

The `Load` methods take `FontResourceLimits`: a file from an untrusted source is bounded like a font. The multiple master
(AMFM) and composite (ACFM) metrics files are not supported.
