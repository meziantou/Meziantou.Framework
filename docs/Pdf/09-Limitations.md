---
title: Limitations
description: What the library does not do, so that you can plan around it.
---

## Creating documents

- The content builder breaks the lines of a text box and of the cells of a table with the Unicode line breaking
  algorithm, without a dictionary: the languages written without spaces between their words (Thai, Lao, Khmer,
  Burmese) are broken between their characters only when a word does not fit. It does not apply the bidirectional
  algorithm to the lines.
- The cells of a table hold text: no image, nested table or cell that spans several rows. The justified lines stretch
  the spaces only.
- The standard 14 fonts show the characters of `WinAnsiEncoding` (Symbol and ZapfDingbats their own); other characters
  need an embedded font.
- The fonts with CFF2 outlines, the bitmap fonts and the instances of variable fonts are not embedded.
- The API reads, fills and flattens interactive form fields, but does not create them; the appearances it writes for
  the values of the fields use the standard 14 fonts.
- A page copied from another document is not tagged: the structure tree belongs to the document of the page.

## Reading and extracting

- The documents encrypted for certificates (the public-key security handler) need RSA certificates: the elliptic-curve
  recipients (key agreement) are not supported. Their crypt filters share one key: when the recipients are in several
  crypt filters, those of the streams are used.
- The text extraction does not recognize the text of scanned pages. Its reading order of the layout
  ([Read the columns in order](05-ReadingAndModifying/03-ExtractingText.md#read-the-columns-in-order)) finds the columns
  from the blank spaces of the page: it reads them from left to right, and keeps the order of the content for the text
  written vertically.
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
- A visible signature is a new signature field: an existing signature field that is not signed yet keeps its widget,
  and the library does not draw an appearance for it.
- The verification of a signature does not decide whether the signer is trusted: the application builds the certificate
  chain.
- The writer encrypts the embedded files with the method of the other streams.
- A linearized file cannot be encrypted, has no object streams, and is built in memory before it is written. It has no
  thumbnail hint table: the thumbnails of the pages, if any, are counted with the objects of their pages.

## Rendering

- The renderer composites the page in sRGB, for a screen: overprint (`OP`, `op`, `OPM`) is not simulated, and the
  other parameters of the output device (black generation, undercolor removal) are ignored. The colors of prepress
  documents that rely on them can differ from an overprint preview.
- The halftones are ignored unless the options ask for them. They are then applied to the red, green and blue
  components of the image, to fully opaque objects only; the screens of the other colorants, `AccurateScreens` and the
  named device halftones are not used (see [Halftones](06-Rendering/05-Halftones.md)).
- The appearances generated for the annotations that have none are simple: the line endings, the rich text and the
  comb fields are not drawn.
