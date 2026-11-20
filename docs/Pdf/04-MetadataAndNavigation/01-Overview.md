---
title: Metadata and navigation
description: Describe a document with its title, author and XMP metadata, help readers find their way with bookmarks and links, and attach files.
---

Besides its pages, a document carries what describes it and what helps readers move in it. The members of
[PdfDocument](cref:Meziantou.Framework.Pdf.PdfDocument) read and write them, for a document you create as for a document you open:

| To | Use | Page |
| --- | --- | --- |
| Give a title, an author, keywords, dates, a language | [Information](cref:Meziantou.Framework.Pdf.PdfDocument.Information), [Language](cref:Meziantou.Framework.Pdf.PdfDocument.Language), the XMP metadata | [Metadata](02-Metadata.md) |
| Add bookmarks | [Outlines](cref:Meziantou.Framework.Pdf.PdfDocument.Outlines) | [Outline and links](03-OutlineAndLinks.md) |
| Link to a page or a website | `PdfPage.Annotations.AddLink` | [Outline and links](03-OutlineAndLinks.md) |
| Attach files to the document or to a page | [EmbeddedFiles](cref:Meziantou.Framework.Pdf.PdfDocument.EmbeddedFiles), `PdfPage.Annotations.AddFileAttachment` | [Embedded files](04-EmbeddedFiles.md) |
| Number the pages i, ii, iii, then 1, 2, 3 | [PageLabels](cref:Meziantou.Framework.Pdf.PdfDocument.PageLabels) | [Page labels](05-PageLabels.md) |
| Add notes, highlights, shapes, ink, free texts and stamps | `PdfPage.Annotations.AddNote`, `AddTextMarkup`, `AddSquare`, `AddInk`, ... | [Annotations](06-Annotations.md) |
| Read and change the annotations of a file, reply to them | The annotation classes, such as [PdfLineAnnotation](cref:Meziantou.Framework.Pdf.PdfLineAnnotation) | [Annotations](06-Annotations.md) |
| Draw the annotations into the content of the pages | [FlattenAnnotations](cref:Meziantou.Framework.Pdf.PdfDocument.FlattenAnnotations) | [Annotations](06-Annotations.md#flatten-the-annotations) |
| Go to a page, open a URI or run a script on a click or an event | [PdfAction](cref:Meziantou.Framework.Pdf.PdfAction), [OpenAction](cref:Meziantou.Framework.Pdf.PdfDocument.OpenAction), `AdditionalActions` | [Actions](07-Actions.md) |
| Put content in layers the user can show or hide | [OptionalContent](cref:Meziantou.Framework.Pdf.PdfDocument.OptionalContent) | [Layers](08-Layers.md) |
| Choose the layout of the pages and the panels viewers show | [PageLayout](cref:Meziantou.Framework.Pdf.PdfDocument.PageLayout), [PageMode](cref:Meziantou.Framework.Pdf.PdfDocument.PageMode), [ViewerPreferences](cref:Meziantou.Framework.Pdf.PdfDocument.ViewerPreferences) | [Viewer preferences](09-ViewerPreferences.md) |
| Show the embedded files as a list with columns | [CreatePortfolio](cref:Meziantou.Framework.Pdf.PdfDocument.CreatePortfolio) | [Portfolios](10-Portfolios.md) |

The changes are written when the document is saved, by a full or an incremental save: see
[Modifying documents](../05-ReadingAndModifying/04-ModifyingDocuments.md).

## In this section

- [Metadata](02-Metadata.md): the document information, the XMP metadata and the language.
- [Outline and links](03-OutlineAndLinks.md): bookmarks, destinations and link annotations.
- [Embedded files](04-EmbeddedFiles.md): attach files to a document or to a page, and read them back.
- [Page labels](05-PageLabels.md): the labels viewers show instead of the page numbers.
- [Annotations](06-Annotations.md): notes, text markups, shapes, ink, free texts and stamps, the annotations of a
  file, replies and pop-ups, flattening, and the flags of the annotations.
- [Actions](07-Actions.md): what viewers do on a click, when the document or a page is opened, and on other events.
- [Layers](08-Layers.md): optional content groups and their configurations.
- [Viewer preferences](09-ViewerPreferences.md): the page layout, the page mode, and the preferences of viewers and
  their print dialog.
- [Portfolios](10-Portfolios.md): show the embedded files as a list, with fields, sorted and in folders.
