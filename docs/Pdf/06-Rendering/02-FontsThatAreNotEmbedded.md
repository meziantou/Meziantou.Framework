---
title: Fonts that are not embedded
description: Choose the fonts that replace the fonts a document does not embed, and render the same pixels on every machine.
---

A document can use a font without embedding it: the standard fonts, such as Helvetica, and fonts the author expected the
reader to have, such as Arial or a corporate font. The renderer replaces each of them, in this order, by:

1. the font [PdfRenderOptions.FontResolver](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderOptions.FontResolver) returns for it;
2. for a standard font (Helvetica, Times, Courier and their aliases, such as Arial), the bundled Liberation font of its
   kind and style, whose glyphs have the same widths, so the text keeps its layout;
3. a font of the system that matches its name, weight and style, then one of its kind (serif, sans serif, fixed pitch),
   unless [PdfRenderOptions.UseSystemFonts](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderOptions.UseSystemFonts) is `false`;
4. the bundled Liberation font of its kind.

The bundled fonts have the characters of the Latin encodings of PDF. Symbol and ZapfDingbats are only replaced by the
resolver or by the fonts of the system.

## Give the fonts of the application

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfRendererTests.cs;region=PdfRendering_FontResolver;format=md-fenced-code-block;dedent=true -->
```csharp
// A font of the application, loaded once
var corporateSans = FontLoader.Load(fontData, new PooledFontEnvironment(FontResourceLimits.Default, maxLiveAllocationBytes: 64 * 1024 * 1024))[0];

var options = new PdfRenderOptions
{
    // Called for each font a document does not embed; null lets the library choose
    FontResolver = request => request.Name.StartsWith("Corporate", StringComparison.Ordinal) ? corporateSans : null,
    UseSystemFonts = false,
};

var document = PdfDocument.Open(file);
using var image = document.Pages[0].Render(options);
```
<!-- endref -->

- The resolver is called for each font the document does not embed, the standard fonts included, with a
  [PdfFontRequest](cref:Meziantou.Framework.Pdf.Rendering.PdfFontRequest): its [Name](cref:Meziantou.Framework.Pdf.Rendering.PdfFontRequest.Name) (the PostScript name, without the tag of a subset), [StandardName](cref:Meziantou.Framework.Pdf.Rendering.PdfFontRequest.StandardName) (the standard font it
  is or is an alias of, such as `Helvetica-Bold` for `Arial,Bold`), [IsBold](cref:Meziantou.Framework.Pdf.Rendering.PdfFontRequest.IsBold), [IsItalic](cref:Meziantou.Framework.Pdf.Rendering.PdfFontRequest.IsItalic), [IsSerif](cref:Meziantou.Framework.Pdf.Rendering.PdfFontRequest.IsSerif), [IsFixedPitch](cref:Meziantou.Framework.Pdf.Rendering.PdfFontRequest.IsFixedPitch) and
  [IsSymbolic](cref:Meziantou.Framework.Pdf.Rendering.PdfFontRequest.IsSymbolic).
- It returns a [FontFace](cref:Meziantou.Framework.Fonts.FontFace) of `Meziantou.Framework.Fonts`, or `null` to let the renderer choose. Load the faces once, and
  share them between renderings.

## Render the same pixels everywhere

With [UseSystemFonts](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderOptions.UseSystemFonts) set to `false`, the fonts of the machine are never used: a page renders the same pixels on a
developer machine, a server and a container, whatever fonts they have installed. Set it for thumbnails that are
compared in tests, cached, or generated on several machines. The documents that embed their fonts do not depend on it.
