---
title: Viewer preferences
description: Choose how viewers show the document when it is opened, the panel they show next to the pages, and the settings of their print dialog.
---

## Set the viewer preferences

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfNavigationTests.cs;region=Pdf_ViewerPreferences;format=md-fenced-code-block;dedent=true -->
```csharp
document.PageLayout = PdfPageLayout.TwoPageRight;
document.PageMode = PdfPageMode.UseOutlines;
var preferences = document.ViewerPreferences;
preferences.DisplayDocumentTitle = true;
preferences.HideToolbar = true;
preferences.Direction = PdfReadingDirection.RightToLeft;
preferences.DisablePrintScaling = true;
preferences.Duplex = PdfDuplexMode.DuplexFlipLongEdge;
preferences.PrintPageRanges = [(0, 0), (2, 4)];
preferences.NumberOfCopies = 2;
```
<!-- endref -->

| Property | Effect |
| --- | --- |
| [PdfDocument.PageLayout](cref:Meziantou.Framework.Pdf.PdfDocument.PageLayout) | One page at a time, one column, or two pages side by side |
| [PdfDocument.PageMode](cref:Meziantou.Framework.Pdf.PdfDocument.PageMode) | The panel next to the pages: the bookmarks, the thumbnails, the layers, the attachments, or full screen |
| [HideToolbar](cref:Meziantou.Framework.Pdf.PdfViewerPreferences.HideToolbar), [HideMenubar](cref:Meziantou.Framework.Pdf.PdfViewerPreferences.HideMenubar), [HideWindowUI](cref:Meziantou.Framework.Pdf.PdfViewerPreferences.HideWindowUI) | Hide the elements of the viewer |
| [FitWindow](cref:Meziantou.Framework.Pdf.PdfViewerPreferences.FitWindow), [CenterWindow](cref:Meziantou.Framework.Pdf.PdfViewerPreferences.CenterWindow) | Resize the window to the first page, center it |
| [DisplayDocumentTitle](cref:Meziantou.Framework.Pdf.PdfViewerPreferences.DisplayDocumentTitle) | Show the title of the document instead of the file name, which PDF/UA requires |
| [Direction](cref:Meziantou.Framework.Pdf.PdfViewerPreferences.Direction) | The reading order, which sets the side of the pages shown side by side |
| [DisablePrintScaling](cref:Meziantou.Framework.Pdf.PdfViewerPreferences.DisablePrintScaling), [Duplex](cref:Meziantou.Framework.Pdf.PdfViewerPreferences.Duplex), [PrintPageRanges](cref:Meziantou.Framework.Pdf.PdfViewerPreferences.PrintPageRanges), [NumberOfCopies](cref:Meziantou.Framework.Pdf.PdfViewerPreferences.NumberOfCopies) | The settings of the print dialog |

- The page ranges are zero-based page indexes, both ends included; the file stores page numbers.
- The dictionary is created when a value is set, and removed when the last one is reset.
- [EnforcedPreferences](cref:Meziantou.Framework.Pdf.PdfViewerPreferences.EnforcedPreferences) lists the preferences viewers must not let the user override.
- Viewers may ignore any of these preferences.
