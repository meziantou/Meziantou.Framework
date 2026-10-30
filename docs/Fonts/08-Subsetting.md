---
title: Subsetting
description: Keep only the glyphs a document uses, to embed a smaller font program in a PDF file or another document.
---

A document that embeds a font, such as a PDF file, only needs the glyphs it draws. [FontSubsetter](cref:Meziantou.Framework.Fonts.FontSubsetter) makes a font program
with these glyphs only:

<!-- ref:../../tests/Meziantou.Framework.Imaging.Tests/FontTests.cs;region=Fonts_Subset;format=md-fenced-code-block;dedent=true -->
```csharp
static byte[] Subset(FontFace face, string text)
{
    // The glyphs the document uses; the closure adds the glyph 0 and the components of composite glyphs
    var glyphs = new HashSet<int>();
    foreach (var rune in text.EnumerateRunes())
    {
        glyphs.Add(face.MapCodePoint(rune.Value));
    }

    // A font program to embed in a document that refers to the glyphs by their identifiers, such as a PDF file:
    // the identifiers are kept, the other glyphs are empty, and there is no character map
    return face.Outlines is FontOutlineKind.TrueType
        ? FontSubsetter.SubsetTrueType(face, glyphs, keepHinting: false)
        : throw new NotSupportedException("SubsetCff gives the CFF table of a font with CFF outlines");
}
```
<!-- endref -->

- The glyph identifiers are kept: the glyphs that are not kept are empty, so that what refers to a glyph by its
  identifier, such as a PDF content stream or a shaped text, is unchanged.
- [GetClosure](cref:Meziantou.Framework.Fonts.FontSubsetter.GetClosure) gives the glyphs a subset keeps: the requested glyphs, the glyph 0 (`.notdef`) and the components of the
  composite glyphs. Identifiers that are not glyphs of the face are ignored.

| Method | Outlines | Result |
| --- | --- | --- |
| [SubsetTrueType](cref:Meziantou.Framework.Fonts.FontSubsetter.SubsetTrueType) | TrueType ([FontOutlineKind.TrueType](cref:Meziantou.Framework.Fonts.FontOutlineKind.TrueType)) | A font file with the `glyf` and `loca` tables rebuilt, the metrics tables (`head`, `hhea`, `hmtx`, `maxp`), `OS/2` and `name`, a `post` table without glyph names, and the instructions when `keepHinting` is set |
| [SubsetCff](cref:Meziantou.Framework.Fonts.FontSubsetter.SubsetCff) | CFF ([FontOutlineKind.Cff](cref:Meziantou.Framework.Fonts.FontOutlineKind.Cff)) | A CID-keyed `CFF` table whose CIDs are the glyph identifiers: the charstrings of the glyphs that are not kept end at once, and the subroutines are kept whole |

The result is a font program to embed, not a font to install: it has no character map and no layout tables. A method
called with a face whose outlines are of the other kind throws an `ArgumentException`.
