---
title: Page labels
description: Label the pages of a document, such as Roman numerals for the front matter and a prefix for an appendix, and read the labels of a file.
---

Viewers show the labels of the pages instead of their numbers. [PageLabels](cref:Meziantou.Framework.Pdf.PdfDocument.PageLabels) holds ranges of pages, each with a numbering
style, a prefix and the number of its first page:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfPageTests.cs;region=Pdf_PageLabels;format=md-fenced-code-block;dedent=true -->
```csharp
// i, ii, iii for the front matter, then 1, 2, 3... and A-1, A-2 for the appendix
document.PageLabels.Set(0, new PdfPageLabel(PdfPageLabelStyle.LowercaseRoman));
document.PageLabels.Set(3, new PdfPageLabel(PdfPageLabelStyle.Decimal));
document.PageLabels.Set(6, new PdfPageLabel(PdfPageLabelStyle.Decimal, prefix: "A-"));
```
<!-- endref -->

- A range starts at the page given to [Set](cref:Meziantou.Framework.Pdf.PdfPageLabelCollection.Set) and ends before the next range. [Remove](cref:Meziantou.Framework.Pdf.PdfPageLabelCollection.Remove) and [Clear](cref:Meziantou.Framework.Pdf.PdfPageLabelCollection.Clear) remove ranges.
- [PdfPageLabelStyle](cref:Meziantou.Framework.Pdf.PdfPageLabelStyle) is decimal, uppercase or lowercase Roman numerals, uppercase or lowercase letters (A to Z, then
  AA to ZZ...), or none: the label is then the prefix alone.
- [PdfPage.Label](cref:Meziantou.Framework.Pdf.PdfPage.Label) and [GetLabel](cref:Meziantou.Framework.Pdf.PdfPageLabelCollection.GetLabel) give the label of a page; it is `null` when the document has no labels.
- The labels are written to the document at once, as one node of a number tree; the labels of a file are read from its
  whole tree.
