---
title: Portfolios
description: Make a document a portfolio, whose embedded files viewers show as a list with columns, sorted and in folders.
---

## Create a portfolio

A portfolio is a document whose embedded files viewers show as a list, instead of the pages. The columns are the
fields of its schema, and each file has a value for each field:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfNavigationTests.cs;region=Pdf_Portfolio;format=md-fenced-code-block;dedent=true -->
```csharp
var report = document.EmbeddedFiles.Add("report.pdf", "%PDF-1.7"u8);
var data = document.EmbeddedFiles.Add("data.csv", "a,b"u8);
var portfolio = document.CreatePortfolio();
portfolio.View = PdfPortfolioView.Details;
portfolio.InitialFile = "report.pdf";
portfolio.AddField("name", "Name", PdfPortfolioFieldType.FileName);
portfolio.AddField("owner", "Owner");
portfolio.AddField("reviewed", "Reviewed", PdfPortfolioFieldType.Date);
portfolio.SetSort(ascending: true, "owner", "name");
report.SetPortfolioValue("owner", "Finance");
report.SetPortfolioValue("reviewed", new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero));
data.SetPortfolioValue("owner", "IT");
```
<!-- endref -->

- The files are the embedded files of the document ([EmbeddedFiles](cref:Meziantou.Framework.Pdf.PdfDocument.EmbeddedFiles)): see [Embedded files](04-EmbeddedFiles.md).
- [AddField](cref:Meziantou.Framework.Pdf.PdfPortfolio.AddField) adds a column: a text, a date or a number set per file, or a property of the file (its name, its
  description, its dates, its size).
- [SetPortfolioValue](cref:Meziantou.Framework.Pdf.PdfEmbeddedFile.SetPortfolioValue) sets the value of a field for a file, and [GetPortfolioValue](cref:Meziantou.Framework.Pdf.PdfEmbeddedFile.GetPortfolioValue) reads it.
- [View](cref:Meziantou.Framework.Pdf.PdfPortfolio.View) chooses a list, tiles, or the initial file alone; [InitialFile](cref:Meziantou.Framework.Pdf.PdfPortfolio.InitialFile) is the file viewers show first.
- [SetSort](cref:Meziantou.Framework.Pdf.PdfPortfolio.SetSort) sorts the files by fields.

## Read a portfolio

[PdfDocument.Portfolio](cref:Meziantou.Framework.Pdf.PdfDocument.Portfolio) is `null` when the document is not a portfolio. The folders of a portfolio are read from
[RootFolder](cref:Meziantou.Framework.Pdf.PdfPortfolio.RootFolder): each folder has a name, an identifier, and its sub-folders. The tree is bounded: a folder reached twice ends a
list, and the depth is limited to 32.
