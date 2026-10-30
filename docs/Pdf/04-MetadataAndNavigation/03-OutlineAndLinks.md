---
title: Outline and links
description: Add bookmarks to a document, link to a page or a website, choose how the target is shown, and read the bookmarks and links of a document.
---

## Add bookmarks and links

The outline (the bookmarks readers show beside the pages) is a tree of items, each with a title and a destination. A
link is a rectangle of a page that goes to a destination or opens a web address:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_OutlineAndLinks;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new PdfDocument();
var cover = document.Pages.Add(PdfSize.A4);
var content = document.Pages.Add(PdfSize.A4);

var chapter = document.Outlines.Add("Chapter 1", PdfDestination.Fit(content));
chapter.Children.Add("Section 1.1", PdfDestination.Xyz(content, left: 0, top: 500));

cover.Annotations.AddLink(new PdfRectangle(50, 50, 250, 80), PdfDestination.Fit(content));
cover.Annotations.AddLink(new PdfRectangle(50, 100, 250, 130), new Uri("https://www.meziantou.net"));
```
<!-- endref -->

- `Outlines.Add` adds an item at the top level, and `Children.Add` an item under another one.
- `Annotations.AddLink` adds a link to a page: the rectangle is the area that can be clicked, in the coordinates of the
  page. A link is invisible: draw what it is on, such as an underlined text, with the content builder.

## Destinations

A destination is a page and how it is shown:

| Destination | Shows |
| --- | --- |
| `PdfDestination.Fit(page)` | The whole page in the window |
| `PdfDestination.FitHorizontally(page, top)` | The width of the page, from a vertical position |
| `PdfDestination.FitVertically(page, left)` | The height of the page, from a horizontal position |
| `PdfDestination.FitRectangle(page, rectangle)` | A rectangle of the page |
| `PdfDestination.Xyz(page, left, top, zoom)` | A point at the upper-left corner of the window, with a zoom (1 is 100%) |
| `PdfDestination.FitBoundingBox(page, kind, coordinate)` | The bounding box of the content of the page, its width or its height |

A coordinate or a zoom left `null` keeps the current one of the reader. PDF/UA-2 requires destinations that also name
the structure element they show: see `WithStructureElement` in
[Tagged PDF and PDF/UA](../08-AccessibilityAndArchiving/02-TaggedPdfAndPdfUA.md#pdfua-2).

## Style the outline

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_OutlineStyles;format=md-fenced-code-block;dedent=true -->
```csharp
var summary = document.Outlines.Add("Summary", PdfDestination.FitHorizontally(page, top: 800));
summary.IsBold = true;
summary.Color = PdfColor.FromRgb(0.6, 0, 0); // an RGB color
summary.IsOpen = true; // its children are shown when the document is opened
summary.Children.Add("Key figures", PdfDestination.Xyz(page, left: 0, top: 600, zoom: 1.5));

// The outline is a list: items can be inserted, moved and removed
document.Outlines.Insert(0, new PdfOutlineItem("Cover", PdfDestination.Fit(page)) { IsItalic = true });
```
<!-- endref -->

- `IsBold`, `IsItalic` and `Color` (an RGB color, `null` for black) style the title.
- `IsOpen` shows the children of the item when the document is opened.
- `Outlines` and `Children` are lists: `Insert`, `Remove`, `RemoveAt`, `Clear` and the indexer change them.

## Read the bookmarks and links

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_ReadLinks;format=md-fenced-code-block;dedent=true -->
```csharp
var document = PdfDocument.Open(file);

// The outline: a tree of items, each with its title and destination
var bookmarks = new List<string>();
foreach (var item in document.Outlines)
{
    bookmarks.Add(item.Title);
    bookmarks.AddRange(item.Children.Select(child => "  " + child.Title));
}

// The links of a page: a web address or a destination in the document
var targets = new List<string>();
foreach (var link in document.Pages[0].Annotations.OfType<PdfLinkAnnotation>())
{
    if (link.Uri is not null)
    {
        targets.Add(link.Uri.AbsoluteUri);
    }
    else if (link.Destination is not null)
    {
        targets.Add("page " + (document.Pages.IndexOf(link.Destination.Page) + 1));
    }
}
```
<!-- endref -->

- The `Destination` of an item is `null` when it has none, or when it has an action of another kind; the other entries of
  an item read from a file, such as its action, are kept when the document is saved.
- The `Uri` of a link is `null` when the link opens no web address or when its address is not a valid URI; its
  `Destination` is set when it goes to a page of the document.
- `PdfPage.Annotations` holds every annotation of the page: links (`PdfLinkAnnotation`), file attachments
  (`PdfFileAttachmentAnnotation`), and the other kinds as `PdfAnnotation`, with their `Subtype`, `Rectangle`,
  `Contents` and `Dictionary`. `Remove` removes one.
