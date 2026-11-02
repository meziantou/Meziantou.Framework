---
title: Layers, annotations and printing
description: Choose the layers (optional content) drawn, render a page for printing, and understand how the annotations are drawn.
---

## Render for printing

[Intent](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderOptions.Intent) says what the image is for:
[PdfRenderIntent.View](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderIntent.View) (the default) for a screen,
[PdfRenderIntent.Print](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderIntent.Print) for paper, and
[PdfRenderIntent.Export](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderIntent.Export) for another format. It selects:

- the annotations: for printing, only the annotations with the Print flag are drawn, NoView ones included; for the
  others, the annotations without the NoView flag. Hidden annotations are never drawn;
- the layers whose usage the document sets for the event (the `/AS` entries of its configuration): a watermark layer
  shown only on paper, or a layer of comments hidden when printed.

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfRendererTests.cs;region=PdfRendering_Print;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new PdfRenderOptions
{
    // The annotations and the layers meant for printing
    Intent = PdfRenderIntent.Print,

    // A layer turned off, whatever the document says
    OptionalContentGroupStates = new Dictionary<string, bool>(StringComparer.Ordinal) { ["Watermark"] = false },
    Dpi = 300,
};
using var image = page.Render(options);
```
<!-- endref -->

## Choose the layers

The layers of a document are its optional content groups. Which are visible is set by a configuration: the default one,
or one of the alternate configurations the document lists, which
[OptionalContentConfiguration](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderOptions.OptionalContentConfiguration)
selects by its name (an unknown name keeps the default configuration). Then
[OptionalContentGroupStates](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderOptions.OptionalContentGroupStates) turns
groups on or off by their name, whatever the configuration says; every group of a name is changed.

The configuration also has intents: a group whose intent is not one of them (a group for design, in a configuration for
viewing) hides nothing. A group with a zoom usage is visible at the magnifications it gives, 100% being 72 dpi.

## Annotations

With [RenderAnnotations](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderOptions.RenderAnnotations), the normal
appearance of each annotation is drawn over the page:

- the appearance is fitted to the rectangle of the annotation, and drawn as a transparency group when it is one, its
  blend modes and soft masks isolated from the page as the group says;
- the constant opacity of the annotation (`/CA`) applies to the appearance as a whole;
- an annotation with the NoZoom flag keeps the size it has at 72 dpi, and one with the NoRotate flag is not turned with
  the page: both keep the upper-left corner of their rectangle in place.

An annotation without appearance gets one, generated from its entries: squares, circles, lines, polygons, polylines,
ink, highlights (multiplied with the page), underlines, strikeouts and squiggly underlines, and the widgets of text,
choice and check box fields (their background and border, and their value with the font and color of their default
appearance). When the interactive form asks for new appearances (`/NeedAppearances`), the text and choice fields get a
generated appearance even if they have one. The generated appearances are simple: the line endings, the rich text and the
comb fields are not drawn.
