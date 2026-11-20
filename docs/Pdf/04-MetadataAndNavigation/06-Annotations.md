---
title: Annotations
description: Add notes, highlights, shapes, ink, free texts and stamps to the pages, read and change the annotations of a file, reply to them, and flatten them into the content.
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
| [AddLine](cref:Meziantou.Framework.Pdf.PdfAnnotationCollection.AddLine) | A straight line, with endings such as arrows |
| [AddSquare](cref:Meziantou.Framework.Pdf.PdfAnnotationCollection.AddSquare), [AddCircle](cref:Meziantou.Framework.Pdf.PdfAnnotationCollection.AddCircle) | A rectangle or an ellipse, with a border and an optional fill |
| [AddPolygon](cref:Meziantou.Framework.Pdf.PdfAnnotationCollection.AddPolygon), [AddPolyLine](cref:Meziantou.Framework.Pdf.PdfAnnotationCollection.AddPolyLine) | A closed shape, or connected lines with endings |
| [AddInk](cref:Meziantou.Framework.Pdf.PdfAnnotationCollection.AddInk) | Freehand strokes, as drawn with a pen |
| [AddCaret](cref:Meziantou.Framework.Pdf.PdfAnnotationCollection.AddCaret) | A symbol that marks where text is to be inserted |
| [AddRedaction](cref:Meziantou.Framework.Pdf.PdfAnnotationCollection.AddRedaction) | Regions to remove, applied later: see [Redaction](../07-Security/05-Redaction.md#mark-regions-to-redact) |

- Each annotation has an appearance stream, so that every viewer shows and prints it the same way. A highlight is
  multiplied with the content, so that the text under it stays readable.
- The markup annotations have an [Author](cref:Meziantou.Framework.Pdf.PdfMarkupAnnotation.Author), a [Subject](cref:Meziantou.Framework.Pdf.PdfMarkupAnnotation.Subject), a [CreationDate](cref:Meziantou.Framework.Pdf.PdfMarkupAnnotation.CreationDate) and an [Opacity](cref:Meziantou.Framework.Pdf.PdfMarkupAnnotation.Opacity).
- [Color](cref:Meziantou.Framework.Pdf.PdfAnnotation.Color) is the color of any annotation: the border of a shape, the color of a line or of a text markup.

## Shapes, lines and ink

The shapes, the lines and the ink are drawn from their geometry. The lines and the polylines can end with arrows,
circles, squares... ([PdfLineEnding](cref:Meziantou.Framework.Pdf.PdfLineEnding)):

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfNavigationTests.cs;region=Pdf_ShapeAnnotations;format=md-fenced-code-block;dedent=true -->
```csharp
var annotations = page.Annotations;
var arrow = annotations.AddLine(new PdfPoint(100, 600), new PdfPoint(250, 680), endEnding: PdfLineEnding.ClosedArrow);
arrow.Contents = "Look here";
annotations.AddSquare(new PdfRectangle(260, 660, 400, 720), PdfColor.FromRgb(0, 0, 0.8), interiorColor: PdfColor.FromRgb(0.9, 0.9, 1));
annotations.AddCircle(new PdfRectangle(100, 450, 200, 520), width: 2);
annotations.AddPolygon([new PdfPoint(300, 450), new PdfPoint(400, 450), new PdfPoint(350, 530)]);
annotations.AddInk([[new PdfPoint(100, 300), new PdfPoint(150, 350), new PdfPoint(200, 300)]], PdfColor.FromRgb(0, 0.6, 0), width: 3);

// A reply in the thread of the arrow, and the window that shows its text
annotations.AddReply(arrow, "Fixed in the next version", author: "Author");
annotations.AddPopup(arrow, new PdfRectangle(260, 560, 460, 640));
```
<!-- endref -->

The rectangle of a line, a polygon or an ink annotation is computed from its points, with room for its width and its
endings.

## Read the annotations of a file

Each annotation of a page is an instance of the class of its type, so a pattern match gives its properties:

| Type (`/Subtype`) | Class | Properties |
| --- | --- | --- |
| `Text` | [PdfTextAnnotation](cref:Meziantou.Framework.Pdf.PdfTextAnnotation) | The icon, whether it is open, the review state |
| `FreeText` | [PdfFreeTextAnnotation](cref:Meziantou.Framework.Pdf.PdfFreeTextAnnotation) | The default appearance, the alignment, the callout line |
| `Line` | [PdfLineAnnotation](cref:Meziantou.Framework.Pdf.PdfLineAnnotation) | The start and the end, the endings, the leader lines |
| `Square`, `Circle` | [PdfSquareAnnotation](cref:Meziantou.Framework.Pdf.PdfSquareAnnotation), [PdfCircleAnnotation](cref:Meziantou.Framework.Pdf.PdfCircleAnnotation) | The interior color |
| `Polygon`, `PolyLine` | [PdfPolygonAnnotation](cref:Meziantou.Framework.Pdf.PdfPolygonAnnotation), [PdfPolyLineAnnotation](cref:Meziantou.Framework.Pdf.PdfPolyLineAnnotation) | The vertices, the endings of a polyline |
| `Ink` | [PdfInkAnnotation](cref:Meziantou.Framework.Pdf.PdfInkAnnotation) | The strokes |
| `Highlight`, `Underline`, `Squiggly`, `StrikeOut` | [PdfTextMarkupAnnotation](cref:Meziantou.Framework.Pdf.PdfTextMarkupAnnotation) | The kind, the regions |
| `Stamp` | [PdfStampAnnotation](cref:Meziantou.Framework.Pdf.PdfStampAnnotation) | The name of the stamp |
| `Caret` | [PdfCaretAnnotation](cref:Meziantou.Framework.Pdf.PdfCaretAnnotation) | Whether it is a paragraph symbol |
| `Redact` | [PdfRedactAnnotation](cref:Meziantou.Framework.Pdf.PdfRedactAnnotation) | The regions, the color and the text that replace them |
| `Popup` | [PdfPopupAnnotation](cref:Meziantou.Framework.Pdf.PdfPopupAnnotation) | The parent, whether it is open |
| `Widget` | [PdfWidgetAnnotation](cref:Meziantou.Framework.Pdf.PdfWidgetAnnotation) | The appearance state, the highlight mode, the colors and captions ([AppearanceCharacteristics](cref:Meziantou.Framework.Pdf.PdfWidgetAnnotation.AppearanceCharacteristics)) |
| `Link` | [PdfLinkAnnotation](cref:Meziantou.Framework.Pdf.PdfLinkAnnotation) | The URI or the destination: see [Outline and links](03-OutlineAndLinks.md) |
| `FileAttachment` | [PdfFileAttachmentAnnotation](cref:Meziantou.Framework.Pdf.PdfFileAttachmentAnnotation) | The file: see [Embedded files](04-EmbeddedFiles.md) |

The sound annotations are [PdfMarkupAnnotation](cref:Meziantou.Framework.Pdf.PdfMarkupAnnotation), and the annotations of other types [PdfAnnotation](cref:Meziantou.Framework.Pdf.PdfAnnotation): read their
[Dictionary](cref:Meziantou.Framework.Pdf.PdfAnnotation.Dictionary).

## Change an annotation

The properties read and write the annotation dictionary. Every annotation also has a
[UniqueName](cref:Meziantou.Framework.Pdf.PdfAnnotation.UniqueName), a [ModificationDate](cref:Meziantou.Framework.Pdf.PdfAnnotation.ModificationDate) and a [Border](cref:Meziantou.Framework.Pdf.PdfAnnotation.Border): its width, its style (solid, dashed, beveled,
inset, underline), its dashes and its cloudy effect.

Viewers draw the appearance of an annotation, not its properties: after changing the geometry, the colors or the border
of an annotation, call [UpdateAppearance](cref:Meziantou.Framework.Pdf.PdfAnnotation.UpdateAppearance) to draw it again. It draws the shapes, the lines, the ink, the
text markups, the notes, the free texts, the stamps, the carets and the redactions, and returns `false` for the other
types (links, file attachments, sounds...), whose appearance is left as it is.

## Replies and pop-ups

A markup annotation can have a pop-up window that shows its text, and replies, which viewers list in its thread:

- [AddPopup](cref:Meziantou.Framework.Pdf.PdfAnnotationCollection.AddPopup) adds the pop-up window of an annotation and sets its [Popup](cref:Meziantou.Framework.Pdf.PdfMarkupAnnotation.Popup).
- [AddReply](cref:Meziantou.Framework.Pdf.PdfAnnotationCollection.AddReply) adds a note in reply to an annotation. The reply has an empty appearance, so that it does not
  draw a second icon.
- [InReplyTo](cref:Meziantou.Framework.Pdf.PdfMarkupAnnotation.InReplyTo) and [ReplyType](cref:Meziantou.Framework.Pdf.PdfMarkupAnnotation.ReplyType) give the annotation a reply answers, and [GetReplies](cref:Meziantou.Framework.Pdf.PdfMarkupAnnotation.GetReplies) the replies of an
  annotation on its page.

## Flatten the annotations

Flattening draws the appearance of annotations in the content of their page and removes them, so that they become
content that cannot be changed:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfNavigationTests.cs;region=Pdf_FlattenAnnotations;format=md-fenced-code-block;dedent=true -->
```csharp
// The markup annotations become content of the page; the links are kept
var flattened = document.FlattenAnnotations();
```
<!-- endref -->

- [PdfDocument.FlattenAnnotations](cref:Meziantou.Framework.Pdf.PdfDocument.FlattenAnnotations) flattens every page, [PdfPage.FlattenAnnotations](cref:Meziantou.Framework.Pdf.PdfPage.FlattenAnnotations) one page.
- By default, the markup annotations are flattened, except the redactions. [Filter](cref:Meziantou.Framework.Pdf.PdfAnnotationFlattenOptions.Filter) selects others, such as the
  links or the widgets. The widgets of signed signature fields are never flattened.
- The annotations hidden for the [Intent](cref:Meziantou.Framework.Pdf.PdfAnnotationFlattenOptions.Intent) are removed without being drawn: those with the `NoView` flag for the
  screen, those without the `Print` flag for printing.
- The appearance is drawn with the opacity and in the optional content (the layer) of the annotation. An annotation
  without an appearance gets the one [UpdateAppearance](cref:Meziantou.Framework.Pdf.PdfAnnotation.UpdateAppearance) draws.
- The pop-up of a flattened annotation is removed, and its replies no longer point to it.

To flatten the form fields with their values, use [PdfInteractiveForm.Flatten](cref:Meziantou.Framework.Pdf.PdfInteractiveForm.Flatten): see [Forms](../05-ReadingAndModifying/09-Forms.md).

## Flags

[Flags](cref:Meziantou.Framework.Pdf.PdfAnnotation.Flags) reads and changes the flags of an annotation: whether it is printed, hidden, shown on the screen, locked...
The bits that [PdfAnnotationFlags](cref:Meziantou.Framework.Pdf.PdfAnnotationFlags) does not name are kept. The annotations the library creates are printed.

## Remove an annotation

[Remove](cref:Meziantou.Framework.Pdf.PdfAnnotationCollection.Remove) removes an annotation from its page with what would keep it in a saved file: its pop-up, the
references of the other annotations to it (replies, pop-ups), its entry in the structure tree, and, for a widget, its
entry in its form field.

## The order of the annotations

[PdfPage.Annotations](cref:Meziantou.Framework.Pdf.PdfPage.Annotations) reads the annotations once, and again when the annotations of the page change: through the
collection or the object model. An annotation is the same instance until then, and an indexed loop does not read them
again on each iteration.
