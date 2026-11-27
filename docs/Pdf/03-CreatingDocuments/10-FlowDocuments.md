---
title: Flow documents
description: Build a document from headings, paragraphs, lists, images and tables: the library breaks the text into lines and the blocks into pages, and tags the document for accessibility.
---

## Build a document from blocks

The content builder draws at positions you compute. [PdfFlowDocument](cref:Meziantou.Framework.Pdf.PdfFlowDocument) computes them: you add blocks in reading
order, and [Build](cref:Meziantou.Framework.Pdf.PdfFlowDocument.Build) breaks their text into lines and the blocks into pages:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfFlowDocumentTests.cs;region=Pdf_FlowDocument;format=md-fenced-code-block;dedent=true -->
```csharp
var flow = new PdfFlowDocument(new PdfFlowOptions
{
    Title = "Quarterly report",
    Language = "en-US",
    Footer = (page, count) => $"Page {page} of {count}",
});
flow.TableOfContents()
    .Heading(1, "Summary")
    .Paragraph(text => text
        .Text("Sales grew by ")
        .Text("12%", new PdfTextStyle { Font = PdfFont.GetStandardFont(PdfStandardFont.HelveticaBold) })
        .Text(". See ")
        .Link("the website", new Uri("https://example.com")))
    .List(["North: +8%", "South: +15%"])
    .Heading(1, "Details")
    .Table(table => table
        .Columns(2, 1)
        .HeaderRow("Region", "Sales")
        .Row("North", "1,200")
        .Row("South", "1,500"));

var document = flow.Build();
```
<!-- endref -->

`Build` returns a [PdfDocument](cref:Meziantou.Framework.Pdf.PdfDocument): change it like any other document, then save it.

## Blocks

| Method | Block |
| --- | --- |
| [Heading](cref:Meziantou.Framework.Pdf.PdfFlowContainer.Heading) | A heading of level 1 to 6, in the heading font, kept with the block that follows it |
| [Paragraph](cref:Meziantou.Framework.Pdf.PdfFlowContainer.Paragraph) | A paragraph of text, or of runs of text in several fonts, sizes and colors, with links ([PdfInlineBuilder](cref:Meziantou.Framework.Pdf.PdfInlineBuilder)) |
| [List](cref:Meziantou.Framework.Pdf.PdfFlowContainer.List) | A bulleted or numbered list, whose items hold texts or blocks (nested lists) |
| [Image](cref:Meziantou.Framework.Pdf.PdfFlowContainer.Image) | An image with its alternate description, scaled down to fit the column and the page |
| [Table](cref:Meziantou.Framework.Pdf.PdfFlowContainer.Table) | A table: header rows repeated on each page, cells that span columns and rows and hold blocks |
| [Rule](cref:Meziantou.Framework.Pdf.PdfFlowContainer.Rule), [Spacer](cref:Meziantou.Framework.Pdf.PdfFlowContainer.Spacer) | A horizontal line, an empty space |
| [PageBreak](cref:Meziantou.Framework.Pdf.PdfFlowDocument.PageBreak) | The next block starts a new page |
| [Columns](cref:Meziantou.Framework.Pdf.PdfFlowDocument.Columns) | Blocks laid out in columns, which continue on the next pages |
| [TableOfContents](cref:Meziantou.Framework.Pdf.PdfFlowDocument.TableOfContents) | The headings of levels 1 to 3, with their page numbers and links to them |

- [PdfParagraphStyle](cref:Meziantou.Framework.Pdf.PdfParagraphStyle) sets the alignment (left, center, right, justified), the spaces before and after, the indentation and
  the text style of a paragraph; [PdfTextStyle](cref:Meziantou.Framework.Pdf.PdfTextStyle) the font, size and color of a run.
- The text is broken at the break opportunities of the Unicode line breaking algorithm, and a word longer than the
  line between its characters. A line feed breaks the line.
- A paragraph whose first strong character is right to left (Hebrew, Arabic) is aligned to the right, and an
  embedded font shapes its text.
- A table row moves to the next page or column, with the rows its cells span, when it does not fit the rest of the
  current one. A row taller than a page or a column is split between pages and columns, as browsers do: the content
  of its cells continues in the next column, below the header rows on a new page, and each part of a cell is drawn
  with its borders, background and padding.
- The table of contents needs the pages of the headings: `Build` lays the document out again until they are stable.

## Pages and styles

[PdfFlowOptions](cref:Meziantou.Framework.Pdf.PdfFlowOptions) sets the size of the pages and their margins, the font, size, line height and color of the text, the
heading font, and the space after the blocks. [Header](cref:Meziantou.Framework.Pdf.PdfFlowOptions.Header) and [Footer](cref:Meziantou.Framework.Pdf.PdfFlowOptions.Footer) give the text of the header and of the footer
of each page from its number and the number of pages; they are drawn centered in the margins.

The headings are added to the outline (the bookmarks) unless [CreateOutline](cref:Meziantou.Framework.Pdf.PdfFlowOptions.CreateOutline) is `false`.

## Accessible documents

The document is tagged unless [Tagged](cref:Meziantou.Framework.Pdf.PdfFlowOptions.Tagged) is `false`: its structure tree has a `Document` element with the headings
(`H1` to `H6`), the paragraphs (`P`), the lists (`L`, `LI`, `Lbl`, `LBody`), the figures with their alternate
descriptions, the tables (`Table`, `THead`, `TBody`, `TR`, `TH` with their scope, `TD`, with their spans), the links
and the table of contents (`TOC`, `TOCI`). The borders, rules, headers and footers are artifacts.

For a PDF/UA document, embed the fonts ([Font](cref:Meziantou.Framework.Pdf.PdfFlowOptions.Font)), set the [Title](cref:Meziantou.Framework.Pdf.PdfFlowOptions.Title) and the [Language](cref:Meziantou.Framework.Pdf.PdfFlowOptions.Language), and set
[PdfUAConformance](cref:Meziantou.Framework.Pdf.PdfDocument.PdfUAConformance) on the document `Build` returns: see [Tagged PDF and PDF/UA](../08-AccessibilityAndArchiving/02-TaggedPdfAndPdfUA.md).
The attributes of the structure elements are written with [SetAttribute](cref:Meziantou.Framework.Pdf.PdfStructureElement.SetAttribute), and read with [GetAttribute](cref:Meziantou.Framework.Pdf.PdfStructureElement.GetAttribute).
