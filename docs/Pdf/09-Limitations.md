---
title: Limitations
description: What the library does not do, so that you can plan around it.
---

## Creating documents

- The content builder breaks a text into lines at its spaces ([Wrap a paragraph](03-CreatingDocuments/03-Text.md#wrap-a-paragraph)),
  but it does not apply the Unicode line breaking rules, hyphenate words, nor lay out tables.
- The standard 14 fonts show the characters of `WinAnsiEncoding` (Symbol and ZapfDingbats their own); other characters
  need an embedded font.
- The fonts with CFF2 outlines, the bitmap fonts and the instances of variable fonts are not embedded.
- The API reads, fills and flattens interactive form fields, but does not create them; the appearances it writes for
  the values of the fields use the standard 14 fonts.
- A page copied from another document is not tagged: the structure tree belongs to the document of the page.

## Reading and extracting

- The public-key security handlers (documents encrypted for certificates) are not supported; only the standard security
  handler (passwords) is.
- The text extraction follows the order of the content and joins the glyphs into lines by their positions; it does not
  detect columns, and it does not recognize the text of scanned pages.
- The library reports the permissions of an encrypted document, but does not enforce them.
- A file larger than 2 GB is read with [OpenSeekable](cref:Meziantou.Framework.Pdf.PdfDocument.OpenSeekable): `Open(path)`, `Open(stream)` and `OpenAsync` read the
  file into one array, which cannot be larger than 2 GB. One stream cannot be larger than 2 GB.
- A file larger than 2 GB is written by [Save](cref:Meziantou.Framework.Pdf.PdfDocument.Save), [SaveAsync](cref:Meziantou.Framework.Pdf.PdfDocument.SaveAsync), [SaveIncremental](cref:Meziantou.Framework.Pdf.PdfDocument.SaveIncremental) and
  [SaveIncrementalAsync](cref:Meziantou.Framework.Pdf.PdfDocument.SaveIncrementalAsync). [ToByteArray](cref:Meziantou.Framework.Pdf.PdfDocument.ToByteArray), a linearized file and the signatures are built in one array, so
  they are limited to 2 GB. The offsets of a cross-reference table have 10 digits: a file whose objects are after
  9,999,999,999 bytes needs a cross-reference stream ([UseObjectStreams](cref:Meziantou.Framework.Pdf.PdfSaveOptions.UseObjectStreams)), and cannot be updated incrementally if
  it has a table.

## Conformance and signatures

- The save checks what the conformance levels require of the document as a whole. The content (the order of the
  structure, the alternate descriptions, the colors of the annotations of PDF/A-1, the values of the numbers) must follow
  the other rules of the level: validate the documents with veraPDF.
- Signatures are invisible: the library does not draw an appearance for the signature field. It writes PAdES B-B and B-T
  signatures; the levels with validation data (B-LT, B-LTA) are not written.
- The verification of a signature does not decide whether the signer is trusted: the application builds the certificate
  chain.
- The writer encrypts the embedded files with the method of the other streams.
- A linearized file cannot be encrypted, has no object streams, and is built in memory before it is written. It has no
  thumbnail hint table: the thumbnails of the pages, if any, are counted with the objects of their pages.

## Rendering

- The renderer composites the page in sRGB, for a screen: overprint (`OP`, `op`, `OPM`) is not simulated, and the
  halftones and the other parameters of the output device (black generation, undercolor removal) are ignored. The
  colors of prepress documents that rely on them can differ from an overprint preview.
- The appearances generated for the annotations that have none are simple: the line endings, the rich text and the
  comb fields are not drawn.
