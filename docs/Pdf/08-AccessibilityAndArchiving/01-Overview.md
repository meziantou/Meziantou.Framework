---
title: Accessibility and archiving
description: Write documents that assistive technologies can read (Tagged PDF, PDF/UA) and documents for long-term archiving (PDF/A), and what the library checks for you.
---

Some documents must follow a standard: PDF/UA for documents that everyone, including people who use assistive
technologies, can read, and PDF/A for archives. Set the level on the document, and the save checks what it can of the
rules of the level and writes what the level needs:

| Property | Levels | Page |
| --- | --- | --- |
| [PdfDocument.PdfUAConformance](cref:Meziantou.Framework.Pdf.PdfDocument.PdfUAConformance) | [PdfUA1](cref:Meziantou.Framework.Pdf.PdfUAConformance.PdfUA1), [PdfUA2](cref:Meziantou.Framework.Pdf.PdfUAConformance.PdfUA2) | [Tagged PDF and PDF/UA](02-TaggedPdfAndPdfUA.md) |
| [PdfDocument.PdfAConformance](cref:Meziantou.Framework.Pdf.PdfDocument.PdfAConformance) | [PdfA1B](cref:Meziantou.Framework.Pdf.PdfAConformance.PdfA1B), [PdfA1A](cref:Meziantou.Framework.Pdf.PdfAConformance.PdfA1A), [PdfA2B](cref:Meziantou.Framework.Pdf.PdfAConformance.PdfA2B), [PdfA2U](cref:Meziantou.Framework.Pdf.PdfAConformance.PdfA2U), [PdfA2A](cref:Meziantou.Framework.Pdf.PdfAConformance.PdfA2A), [PdfA3B](cref:Meziantou.Framework.Pdf.PdfAConformance.PdfA3B), [PdfA3U](cref:Meziantou.Framework.Pdf.PdfAConformance.PdfA3U), [PdfA3A](cref:Meziantou.Framework.Pdf.PdfAConformance.PdfA3A), [PdfA4](cref:Meziantou.Framework.Pdf.PdfAConformance.PdfA4), [PdfA4E](cref:Meziantou.Framework.Pdf.PdfAConformance.PdfA4E), [PdfA4F](cref:Meziantou.Framework.Pdf.PdfAConformance.PdfA4F) | [PDF/A](03-PdfA.md) |

Both can be combined, for an accessible archived document: PDF/A-2a and PDF/UA-1 in the example of
[Tagged PDF and PDF/UA](02-TaggedPdfAndPdfUA.md).

## What the save checks, and what it does not

The save checks what concerns the document as a whole (a title, a language, embedded fonts, no encryption, the
structure tree). When a rule is broken, it throws an `InvalidOperationException` whose message lists every rule that is
broken. It writes the identification of the level in the XMP metadata, the output intent of PDF/A, and the viewer
preferences of PDF/UA.

The content itself must follow the other rules of the level: the reading order of the structure, the alternate
descriptions of the figures, the colors. The library's own tests validate the documents it writes with
[veraPDF](https://verapdf.org), the reference validator; validate your documents with it too when conformance matters.

## In this section

- [Tagged PDF and PDF/UA](02-TaggedPdfAndPdfUA.md): the structure tree, artifacts, alternate descriptions, PDF/UA-1 and
  PDF/UA-2.
- [PDF/A](03-PdfA.md): the levels, what each requires, and attached files of PDF/A-3.
