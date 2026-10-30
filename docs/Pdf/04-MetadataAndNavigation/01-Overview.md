---
title: Metadata and navigation
description: Describe a document with its title, author and XMP metadata, help readers find their way with bookmarks and links, and attach files.
---

Besides its pages, a document carries what describes it and what helps readers move in it. The members of
`PdfDocument` read and write them, for a document you create as for a document you open:

| To | Use | Page |
| --- | --- | --- |
| Give a title, an author, keywords, dates, a language | `Information`, `Language`, the XMP metadata | [Metadata](02-Metadata.md) |
| Add bookmarks | `Outlines` | [Outline and links](03-OutlineAndLinks.md) |
| Link to a page or a website | `PdfPage.Annotations.AddLink` | [Outline and links](03-OutlineAndLinks.md) |
| Attach files to the document or to a page | `EmbeddedFiles`, `PdfPage.Annotations.AddFileAttachment` | [Embedded files](04-EmbeddedFiles.md) |

The changes are written when the document is saved, by a full or an incremental save: see
[Modifying documents](../05-ReadingAndModifying/04-ModifyingDocuments.md).

## In this section

- [Metadata](02-Metadata.md): the document information, the XMP metadata and the language.
- [Outline and links](03-OutlineAndLinks.md): bookmarks, destinations and link annotations.
- [Embedded files](04-EmbeddedFiles.md): attach files to a document or to a page, and read them back.
