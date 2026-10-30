---
title: Tagged PDF and PDF/UA
description: Give a document a logical structure that screen readers follow, mark decorations as artifacts, describe figures, and write PDF/UA-1 and PDF/UA-2 documents.
---

A tagged document has a structure tree: the logical structure of its content (headings, paragraphs, figures, tables),
in reading order. Screen readers follow it, and readers use it to reflow the text on small screens. PDF/UA, the standard
of accessible PDF documents, requires it.

## Tag the content

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_TaggedDocument;format=md-fenced-code-block;dedent=true -->
```csharp
var font = PdfFont.Load(fontData);
var document = new PdfDocument
{
    PdfAConformance = PdfAConformance.PdfA2A,
    PdfUAConformance = PdfUAConformance.PdfUA1,
    Language = "en-US",
};
document.Information.Title = "Annual report";

// The logical structure: a heading, a paragraph and a figure in a Document element
var root = document.StructureTree.AddElement("Document");
var heading = root.AddElement("H1");
var paragraph = root.AddElement("P");
var figure = root.AddElement("Figure");
figure.AlternateDescription = "A blue square";

// The content of each element, and the page number as an artifact
document.Pages.Add(PdfSize.A4).CreateContentBuilder()
    .BeginStructureElement(heading).DrawText("AVATAR", font, 24, 60, 760).EndMarkedContent()
    .BeginStructureElement(paragraph).DrawText("Office", font, 12, 60, 720).EndMarkedContent()
    .BeginStructureElement(figure).SetFillRgb(0, 0, 1).Rectangle(60, 600, 100, 100).Fill().EndMarkedContent()
    .BeginArtifact(PdfArtifactType.Pagination).DrawText("1", font, 10, 300, 40).EndMarkedContent();

var pdf = document.ToByteArray();
```
<!-- endref -->

- `StructureTree.AddElement` adds an element at the root of the tree, and `AddElement` of an element a child of it, in
  reading order. The type is a standard structure type: `Document`, `Part`, `Sect`, `H1` to `H6`, `P`, `L` and `LI`
  (lists), `Table`, `TR`, `TH`, `TD`, `Figure`, `Link`...
- `BeginStructureElement` starts the content of an element on a page, until the matching `EndMarkedContent`. It is
  available in the content of a page, not of a form or a pattern.
- `BeginArtifact` marks content that is not part of the structure: page numbers (`Pagination`), headers and decorations
  (`Layout`, `Background`), with `EndMarkedContent` at its end. Screen readers skip it.
- `AlternateDescription` describes a figure, `ActualText` gives the text of content drawn otherwise, `Expansion` the
  expansion of an abbreviation, `Language` the language of an element in another language than the document, and
  `Title` a title of the element.

## Custom types and links

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_TaggedLink;format=md-fenced-code-block;dedent=true -->
```csharp
var font = PdfFont.Load(fontData);
var document = new PdfDocument { PdfUAConformance = PdfUAConformance.PdfUA1, Language = "en-US" };
document.Information.Title = "Contact";
var page = document.Pages.Add(PdfSize.A4);

// A type of the application, mapped to the standard type assistive technologies know
document.StructureTree.MapRole("Lead", "P");
var root = document.StructureTree.AddElement("Document");
var lead = root.AddElement("Lead");
var link = root.AddElement("Link");

// A link is both content and an annotation: the element holds both
var annotation = page.Annotations.AddLink(new PdfRectangle(60, 715, 200, 735), new Uri("https://www.meziantou.net"));
annotation.Contents = "The website of the author";
link.AddAnnotation(annotation);

page.CreateContentBuilder()
    .BeginStructureElement(lead).DrawText("Contact the author", font, 14, 60, 760).EndMarkedContent()
    .BeginStructureElement(link).DrawText("www.meziantou.net", font, 12, 60, 720).EndMarkedContent();

var pdf = document.ToByteArray();
```
<!-- endref -->

- `MapRole` maps a type of the application to a standard type, so that assistive technologies know what it is.
  `RoleMap` lists the mappings.
- A link is content (its text) and an annotation (the area that can be clicked): `AddAnnotation` puts the annotation in
  the element of the link, and the annotation `Contents` describes it.

`StructureTree.Exists` tells whether a document has a structure tree, and `Elements` and `Children` read it, with the
`Type` of each element.

## PDF/UA-1

With `PdfUAConformance = PdfUAConformance.PdfUA1`, the save checks that the document has a structure tree, a title and a
language, and that its fonts are embedded. It writes the PDF/UA identification in the XMP metadata, and asks viewers to
show the title instead of the file name.

The rest is up to the content, as PDF/UA requires: every content is tagged or an artifact, the structure follows the
reading order, every figure has an alternate description, and the headings are nested in order.

## PDF/UA-2

PDF/UA-2 (ISO 14289-2) is based on PDF 2.0, so it is combined with PDF/A-4 only. The save checks that the structure tree
has a single `Document` element and no `H` or `Note` element, puts the standard types of PDF 2.0 in the PDF 2.0
namespace, and refuses the destinations that go to a page only: give them the structure element they show with
`WithStructureElement`. The embedded files need a description.

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_PdfUA2;format=md-fenced-code-block;dedent=true -->
```csharp
var font = PdfFont.Load(fontData);
var document = new PdfDocument
{
    PdfAConformance = PdfAConformance.PdfA4,
    PdfUAConformance = PdfUAConformance.PdfUA2,
    Language = "en-US",
};
document.Information.Title = "Annual report";

// A single Document element, and headings H1 to H6
var root = document.StructureTree.AddElement("Document");
var heading = root.AddElement("H1");
var page = document.Pages.Add(PdfSize.A4);
page.CreateContentBuilder().BeginStructureElement(heading).DrawText("Results", font, 24, 60, 760).EndMarkedContent();

// The destinations in the document go to structure elements
document.Outlines.Add("Results", PdfDestination.Fit(page).WithStructureElement(heading));

var pdf = document.ToByteArray();
```
<!-- endref -->
