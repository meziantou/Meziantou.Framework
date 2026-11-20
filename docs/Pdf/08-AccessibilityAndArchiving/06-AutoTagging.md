---
title: Tagging existing documents
description: Find the headings, paragraphs, lists, tables and figures of untagged pages, and tag the document so that screen readers and PDF/A level a can use it.
---

## Tag a document

Most documents in circulation are not tagged: screen readers cannot tell their headings from their paragraphs, and
they cannot be converted to PDF/A level a. [AutoTag](cref:Meziantou.Framework.Pdf.PdfDocument.AutoTag) finds the layout of the pages and tags the document:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_AutoTag;format=md-fenced-code-block;dedent=true -->
```csharp
var document = PdfDocument.Open(file);
var result = document.AutoTag(new PdfAutoTagOptions
{
    Language = "en-US",
    FigureAlternateText = image => "Company logo",
});
```
<!-- endref -->

- The structure tree gets a `Document` element with the headings (`H1` to `H6`), the paragraphs (`P`), the lists
  (`L`, `LI`, `LBody`), the tables (`Table`, `TR`, `TH` for the first row, `TD`) and the figures, in reading order.
- The content of the pages is rewritten: each operator that shows text or an image is in a marked-content sequence of
  its element. The headers, the footers, the rules and the shapes are artifacts. Only marked-content operators are
  added: the pages look the same.
- [FigureAlternateText](cref:Meziantou.Framework.Pdf.PdfAutoTagOptions.FigureAlternateText) gives the alternate description of each image, which PDF/UA requires; the result counts the
  figures without one ([FiguresWithoutAlternateText](cref:Meziantou.Framework.Pdf.PdfAutoTagResult.FiguresWithoutAlternateText)).
- A document that is already tagged throws `InvalidOperationException`.

Then check the result with [PdfAccessibilityInspector](cref:Meziantou.Framework.Pdf.PdfAccessibilityInspector) ([Inspecting accessibility](05-InspectingAccessibility.md)), correct
the elements of [StructureTree](cref:Meziantou.Framework.Pdf.PdfDocument.StructureTree) when needed, and convert the document to PDF/A level a
([Converting to PDF/A](04-ConvertingToPdfA.md)).

## How the layout is found

[PdfDocument.ExtractLayout](cref:Meziantou.Framework.Pdf.PdfDocument.ExtractLayout) returns the layout without tagging, and [PdfPage.ExtractLayout](cref:Meziantou.Framework.Pdf.PdfPage.ExtractLayout) the layout of one page: its
[PdfLayoutNode](cref:Meziantou.Framework.Pdf.PdfLayoutNode) objects, in reading order.

| Node | Found from |
| --- | --- |
| Heading | A block of at most three lines in a font larger than the body text (the size of most of the text), or a bold line of that size. The levels follow the sizes, the largest first |
| List, ListItem | Lines that start with a bullet, a dash, or a number or a letter followed by a period or a parenthesis |
| Paragraph | The other blocks of text: the blocks of the reading order of the text extraction ([Read the columns in order](../05-ReadingAndModifying/03-ExtractingText.md#read-the-columns-in-order)) |
| Table, TableCell | A grid of rules, read by [PdfTableExtractor](cref:Meziantou.Framework.Pdf.PdfTableExtractor) |
| Figure | An image the page paints |
| Header, Footer | Text repeated at the same place in the top or bottom 15% of half the pages or more; the numbers in it may differ (page numbers) |

[PdfLayoutOptions](cref:Meziantou.Framework.Pdf.PdfLayoutOptions) turns the detection of the headers, footers and tables off, and sets the size ratio of the headings.

## Limits

- The layout is a guess from the positions and the fonts of the text: a document whose headings are in the font of the
  body, or whose tables have no rules, is tagged with paragraphs.
- A text operator is in one element: a `TJ` operator that shows the text of two blocks is in the element of the first
  one, and a form XObject is in the element of its first text.
- The structure follows the reading order; the content is not reordered, so an inspection may report a reading order
  that differs from the order of the content.
