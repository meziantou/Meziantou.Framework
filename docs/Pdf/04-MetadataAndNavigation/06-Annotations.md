---
title: Annotations
description: Add notes, highlights, underlines, free texts and stamps to the pages, and read and change the flags of the annotations.
---

## Add markup annotations

The methods of [PdfPage.Annotations](cref:Meziantou.Framework.Pdf.PdfPage.Annotations) add the annotations a reviewer adds to a document:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfNavigationTests.cs;region=Pdf_MarkupAnnotations;format=md-fenced-code-block;dedent=true -->
```csharp
var annotations = page.Annotations;
var note = annotations.AddNote(new PdfPoint(40, 700), "Check this paragraph");
note.Author = "Reviewer";
annotations.AddTextMarkup(PdfTextMarkupKind.Highlight, [new PdfRectangle(72, 697, 175, 710)]);
annotations.AddFreeText(new PdfRectangle(300, 690, 500, 730), "Free text drawn on the page", fontSize: 10);
annotations.AddStamp(new PdfRectangle(400, 750, 550, 800), "APPROVED", PdfColor.FromRgb(0, 0.5, 0));
```
<!-- endref -->

| Method | Annotation |
| --- | --- |
| [AddNote](cref:Meziantou.Framework.Pdf.PdfAnnotationCollection.AddNote) | A note: an icon that opens a text |
| [AddTextMarkup](cref:Meziantou.Framework.Pdf.PdfAnnotationCollection.AddTextMarkup) | A highlight, an underline, a squiggly underline or a strikeout of regions of the page, one rectangle per line |
| [AddFreeText](cref:Meziantou.Framework.Pdf.PdfAnnotationCollection.AddFreeText) | A text in a box, drawn on the page, in a standard font |
| [AddStamp](cref:Meziantou.Framework.Pdf.PdfAnnotationCollection.AddStamp) | A word in a frame with rounded corners, such as APPROVED or DRAFT |

- Each annotation has an appearance stream, so that every viewer shows and prints it the same way. A highlight is
  multiplied with the content, so that the text under it stays readable.
- They return a [PdfMarkupAnnotation](cref:Meziantou.Framework.Pdf.PdfMarkupAnnotation): set its [Author](cref:Meziantou.Framework.Pdf.PdfMarkupAnnotation.Author), [Subject](cref:Meziantou.Framework.Pdf.PdfMarkupAnnotation.Subject), [CreationDate](cref:Meziantou.Framework.Pdf.PdfMarkupAnnotation.CreationDate) and [Opacity](cref:Meziantou.Framework.Pdf.PdfMarkupAnnotation.Opacity). The markup
  annotations of a file (shapes, ink, lines...) are read as [PdfMarkupAnnotation](cref:Meziantou.Framework.Pdf.PdfMarkupAnnotation) too.
- [Color](cref:Meziantou.Framework.Pdf.PdfAnnotation.Color) is the color of any annotation; changing it does not change an appearance it already has.

## Flags

[Flags](cref:Meziantou.Framework.Pdf.PdfAnnotation.Flags) reads and changes the flags of an annotation: whether it is printed, hidden, shown on the screen, locked...
The bits that [PdfAnnotationFlags](cref:Meziantou.Framework.Pdf.PdfAnnotationFlags) does not name are kept. The annotations the library creates are printed.

## The order of the annotations

[PdfPage.Annotations](cref:Meziantou.Framework.Pdf.PdfPage.Annotations) reads the annotations once, and again when the annotations of the page change: through the
collection or the object model. An annotation is the same instance until then, and an indexed loop does not read them
again on each iteration.
