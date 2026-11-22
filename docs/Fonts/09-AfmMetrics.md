---
title: AFM metrics
description: Read the metrics of PostScript fonts from AFM files, including the embedded metrics of the 14 standard fonts of PDF, to measure text, and get the characters of glyph names.
---

An AFM file (Adobe Font Metrics) gives the metrics of a PostScript font without its glyphs: the widths and the boxes of
its characters, its kerning pairs and its ligatures. A PDF writer uses them to measure text in the 14 standard fonts of
PDF, which PDF readers provide.

## Measure text with a standard font

The metrics of the 14 standard fonts are embedded: [AfmFontMetrics.GetStandardFont](cref:Meziantou.Framework.Fonts.AfmFontMetrics.GetStandardFont) gives them by name, and
[StandardFontNames](cref:Meziantou.Framework.Fonts.AfmFontMetrics.StandardFontNames) lists the names.

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
- [GetStandardFont](cref:Meziantou.Framework.Fonts.AfmFontMetrics.GetStandardFont) returns `null` for another name; the names are case-sensitive (`Helvetica`, not `helvetica`).
- A character is found by its name ([GetCharacter](cref:Meziantou.Framework.Fonts.AfmFontMetrics.GetCharacter), [GetWidth](cref:Meziantou.Framework.Fonts.AfmFontMetrics.GetWidth)) or by its code in the encoding of the font
  ([GetCharacterByCode](cref:Meziantou.Framework.Fonts.AfmFontMetrics.GetCharacterByCode)).

## Read an AFM file

[AfmFontMetrics.Parse](cref:Meziantou.Framework.Fonts.AfmFontMetrics.Parse) reads the text of a file, and [Load](cref:Meziantou.Framework.Fonts.AfmFontMetrics.Load) and [LoadAsync](cref:Meziantou.Framework.Fonts.AfmFontMetrics.LoadAsync) read a stream or bytes:

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
| [FontName](cref:Meziantou.Framework.Fonts.AfmFontMetrics.FontName), [FullName](cref:Meziantou.Framework.Fonts.AfmFontMetrics.FullName), [FamilyName](cref:Meziantou.Framework.Fonts.AfmFontMetrics.FamilyName), [Weight](cref:Meziantou.Framework.Fonts.AfmFontMetrics.Weight), `Version`, [Notice](cref:Meziantou.Framework.Fonts.AfmFontMetrics.Notice) | The names of the font |
| [FontBBox](cref:Meziantou.Framework.Fonts.AfmFontMetrics.FontBBox), [Ascender](cref:Meziantou.Framework.Fonts.AfmFontMetrics.Ascender), [Descender](cref:Meziantou.Framework.Fonts.AfmFontMetrics.Descender), [CapHeight](cref:Meziantou.Framework.Fonts.AfmFontMetrics.CapHeight), [XHeight](cref:Meziantou.Framework.Fonts.AfmFontMetrics.XHeight), [StdHW](cref:Meziantou.Framework.Fonts.AfmFontMetrics.StdHW), [StdVW](cref:Meziantou.Framework.Fonts.AfmFontMetrics.StdVW) | The global metrics |
| [CharacterMetrics](cref:Meziantou.Framework.Fonts.AfmFontMetrics.CharacterMetrics), [GetCharacter](cref:Meziantou.Framework.Fonts.AfmFontMetrics.GetCharacter), [GetCharacterByCode](cref:Meziantou.Framework.Fonts.AfmFontMetrics.GetCharacterByCode), [GetWidth](cref:Meziantou.Framework.Fonts.AfmFontMetrics.GetWidth), [GetVerticalWidth](cref:Meziantou.Framework.Fonts.AfmFontMetrics.GetVerticalWidth) | The characters: code, name, width, box and ligatures |
| [KerningPairs](cref:Meziantou.Framework.Fonts.AfmFontMetrics.KerningPairs), [GetKerning](cref:Meziantou.Framework.Fonts.AfmFontMetrics.GetKerning), [VerticalKerningPairs](cref:Meziantou.Framework.Fonts.AfmFontMetrics.VerticalKerningPairs), [GetVerticalKerning](cref:Meziantou.Framework.Fonts.AfmFontMetrics.GetVerticalKerning) | The kerning of pairs |
| [TrackKerning](cref:Meziantou.Framework.Fonts.AfmFontMetrics.TrackKerning), [GetTrackKerning](cref:Meziantou.Framework.Fonts.AfmFontMetrics.GetTrackKerning) | The kerning that depends on the size |
| [Composites](cref:Meziantou.Framework.Fonts.AfmFontMetrics.Composites) | The characters made of other characters |

The [Load](cref:Meziantou.Framework.Fonts.AfmFontMetrics.Load) methods take [FontResourceLimits](cref:Meziantou.Framework.Fonts.FontResourceLimits): a file from an untrusted source is bounded like a font. The multiple master
(AMFM) and composite (ACFM) metrics files are not supported.

## Get the characters of glyph names

The characters of an AFM file, and the glyphs of Type 1 and CFF fonts, are named after the characters they draw:
`eacute`, `uni20AC`, `f_f_i`. [AdobeGlyphList](cref:Meziantou.Framework.Fonts.AdobeGlyphList) gives the characters of these names, from the Adobe Glyph List
and the naming conventions of the Adobe Glyph List Specification.

<!-- ref:../../tests/Meziantou.Framework.Imaging.Tests/FontTests.cs;region=AdobeGlyphList_GlyphNames;format=md-fenced-code-block;dedent=true -->
```csharp
// The text of the glyph names of PostScript fonts, such as the names of the characters of an AFM file
Assert.Equal("\u00E9", AdobeGlyphList.ToUnicode("eacute"));
Assert.Equal("ffi", AdobeGlyphList.ToUnicode("f_f_i")); // a ligature names its components
Assert.Equal("a", AdobeGlyphList.ToUnicode("a.sc")); // a variant has the characters of its base name
Assert.Equal("\u2701", AdobeGlyphList.ToUnicode("a1", zapfDingbats: true)); // the names of ITC Zapf Dingbats

// A character map maps one character to a glyph: a ligature or a variant has no character
Assert.True(AdobeGlyphList.TryGetCodePoint("uni20AC", out var euro));
Assert.Equal(0x20AC, euro);
Assert.False(AdobeGlyphList.TryGetCodePoint("f_f_i", out _));
```
<!-- endref -->

- [ToUnicode](cref:Meziantou.Framework.Fonts.AdobeGlyphList.ToUnicode) gives the text of a name, which can be several characters: a ligature (`f_f_i`), or a name of the list that
  maps to a sequence. A suffix after a period is a variant of the same characters (`a.sc`). It returns `null` for a name
  with no character, such as `g42`.
- [TryGetCodePoint](cref:Meziantou.Framework.Fonts.AdobeGlyphList.TryGetCodePoint) gives the character of a name of one character only, to build a character map: a ligature or a
  variant has none, so that the map gives the glyph of the character itself. Its `uniXXXX` and `uXXXX` names take
  upper-case hexadecimal digits, as the specification requires.
- The glyphs of ITC Zapf Dingbats have their own names (`a1`, `a2`...): pass `zapfDingbats: true` to read them.
