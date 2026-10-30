---
title: Extracting text
description: Get the text of the pages of a document, search a document for a word, and know what the extraction can and cannot do.
---

[PdfPage.ExtractText](cref:Meziantou.Framework.Pdf.PdfPage.ExtractText) returns the text of a page, with a line break between the lines:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_ReadDocument;format=md-fenced-code-block;dedent=true -->
```csharp
var document = PdfDocument.Open(file);
var pageCount = document.Pages.Count;
var text = document.Pages[0].ExtractText();
```
<!-- endref -->

## Search a document

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_SearchText;format=md-fenced-code-block;dedent=true -->
```csharp
var document = PdfDocument.Open(path);

// The numbers of the pages whose text contains a word
var pages = new List<int>();
for (var i = 0; i < document.Pages.Count; i++)
{
    var text = document.Pages[i].ExtractText();
    if (text.Contains("revenue", StringComparison.OrdinalIgnoreCase))
    {
        pages.Add(i + 1);
    }
}
```
<!-- endref -->

## How the text is extracted

- The glyphs are read in the order of the content, and turned back into characters with the encoding of their font,
  its `ToUnicode` map or the names of its glyphs. The documents the library writes have what the extraction needs,
  embedded fonts included.
- The glyphs are joined into lines by their positions. A gap between two glyphs of a line becomes a space, and a new
  line becomes a line break, even when the document draws each word separately.
- A replacement text (`/ActualText`) replaces the glyphs it covers, such as a ligature or a right-to-left run.
- The text of the forms the page paints is included.

## Limits

- The order is the order of the content: the extraction does not detect columns, and a document that draws the text in
  another order than it is read gives it in that order.
- A text drawn as paths or as an image (a scanned page) has no characters to extract.
- A font without encoding information, such as some embedded fonts of old documents, can give wrong characters.
- A page whose content runs too many operators, such as forms that paint forms many times, throws
  [PdfResourceLimitException](cref:Meziantou.Framework.Pdf.PdfResourceLimitException) instead of running for a long time.
