---
title: Reading and modifying
description: Open existing PDF documents, extract their text, change their pages and metadata, and save them in full or incrementally.
---

`PdfDocument.Open` reads an existing document: every member of `PdfDocument` then works on it as on a document you
create. Pages can be read, drawn on, added and removed; the metadata, the outline and the attachments can be changed;
and the document is saved again.

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfDocumentTests.cs;region=Pdf_ReadDocument;format=md-fenced-code-block;dedent=true -->
```csharp
var document = PdfDocument.Open(file);
var pageCount = document.Pages.Count;
var text = document.Pages[0].ExtractText();
```
<!-- endref -->

## What the reader supports

- Cross-reference tables and streams, hybrid files, object streams, incremental updates and linearized files.
- Every standard filter of the streams: `FlateDecode` and `LZWDecode` (with their predictors), `ASCIIHexDecode`,
  `ASCII85Decode`, `RunLengthDecode`, `CCITTFaxDecode`, `JBIG2Decode`, and `DCTDecode` and `JPXDecode` through the
  codecs of `Meziantou.Framework.Imaging`.
- Encrypted documents of the standard security handler, with the user or the owner password.
- Damaged files: a file whose cross-reference information is wrong is repaired by scanning it, and a stream whose length
  is wrong ends where its data ends.

The objects of a file are parsed when they are first used, not when the document is opened, and the document keeps the
data of the file it was read from.

## In this section

- [Opening documents](02-OpeningDocuments.md): files, streams and arrays, passwords, read options and repaired files.
- [Extracting text](03-ExtractingText.md): the text of a page, and search a document.
- [Modifying documents](04-ModifyingDocuments.md): stamp the pages, add and remove pages, full and incremental saves.
- [The object model](05-ObjectModel.md): read and change the objects of a document directly.
- [Errors and limits](06-ErrorsAndLimits.md): the exceptions, and how to open the files of untrusted users.
