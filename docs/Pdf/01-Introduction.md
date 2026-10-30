---
title: Introduction
description: A fully managed .NET library to create, read, modify, render and sign PDF documents.
---

`Meziantou.Framework.Pdf` creates, reads, modifies, renders and signs PDF documents in .NET. It is written entirely in
C# from the PDF 2.0 specification (ISO 32000-2): it has no native dependency, so it runs the same way on Windows, Linux
and macOS, and in trimmed and NativeAOT applications.

## What you can do with it

- **Create documents**: pages of any size, shapes, colors, text with the standard fonts or with embedded TrueType and
  OpenType fonts, images, gradients, patterns and transparency. See [Creating documents](03-CreatingDocuments/01-Overview.md).
- **Describe and organize them**: the title, the author and the XMP metadata, bookmarks, links and attached files. See
  [Metadata and navigation](04-MetadataAndNavigation/01-Overview.md).
- **Read and modify existing documents**: open files from any producer, repair files whose structure is damaged, extract
  the text, add content to the pages, and append changes without rewriting the original file. See
  [Reading and modifying](05-ReadingAndModifying/01-Overview.md).
- **Render pages to images**: thumbnails and previews, with the embedded fonts, every image format and color space of
  PDF, shadings, patterns and transparency. See [Rendering](06-Rendering/01-Overview.md).
- **Protect documents**: encrypt them with passwords (RC4 and AES up to 256 bits), sign them with PAdES signatures and
  time stamps, and verify their signatures. See [Security](07-Security/01-Overview.md).
- **Write accessible and archival documents**: Tagged PDF, PDF/UA-1 and PDF/UA-2, PDF/A-1 to PDF/A-4. See
  [Accessibility and archiving](08-AccessibilityAndArchiving/01-Overview.md).
- **Go below the API**: every object of a document (dictionaries, arrays, streams) can be read and changed, for what the
  high-level API does not cover. See [The object model](05-ReadingAndModifying/05-ObjectModel.md).

## Principles

- **Real files are damaged.** The reader accepts what PDF viewers accept, and rebuilds the structure of a file whose
  cross-reference information is wrong. [PdfDocument.IsRepaired](cref:Meziantou.Framework.Pdf.PdfDocument.IsRepaired) tells you when it did.
- **Hostile files are bounded.** Everything a file can make grow (the decoded streams, the nesting of objects, the work
  of a page that is rendered) has a limit. A file takes time and memory in proportion to its size.
- **Errors are explicit.** A file that is not a PDF document, a feature the library does not support, an exceeded limit
  and a wrong password each throw a distinct exception. See [Errors and limits](05-ReadingAndModifying/06-ErrorsAndLimits.md).
- **Reproducible output.** A save depends only on the document and the options: the library never writes the current
  date, a random value or its own version unless you ask it to. Only encryption adds random keys.
- **Changes are kept apart from the original.** An incremental save appends the changes to the original file and leaves
  its bytes untouched, so its signatures stay valid.

## Namespaces

| Namespace | Contains |
| --- | --- |
| [Meziantou.Framework.Pdf](cref:Meziantou.Framework.Pdf) | Documents, pages, the content builder, fonts, images, metadata, outline, encryption, conformance |
| [Meziantou.Framework.Pdf.Objects](cref:Meziantou.Framework.Pdf.Objects) | The object model: [PdfDictionary](cref:Meziantou.Framework.Pdf.Objects.PdfDictionary), [PdfArray](cref:Meziantou.Framework.Pdf.Objects.PdfArray), [PdfStream](cref:Meziantou.Framework.Pdf.Objects.PdfStream), [PdfName](cref:Meziantou.Framework.Pdf.Objects.PdfName)... |
| [Meziantou.Framework.Pdf.Rendering](cref:Meziantou.Framework.Pdf.Rendering) | [PdfRenderer](cref:Meziantou.Framework.Pdf.Rendering.PdfRenderer) and its options |
| [Meziantou.Framework.Pdf.Signing](cref:Meziantou.Framework.Pdf.Signing) | [PdfSigner](cref:Meziantou.Framework.Pdf.Signing.PdfSigner), [PdfSignatureVerifier](cref:Meziantou.Framework.Pdf.Signing.PdfSignatureVerifier) and their options |

The library uses `Meziantou.Framework.Imaging` for the images (decoding, color profiles, the rendered pages) and
`Meziantou.Framework.Fonts` for the fonts it embeds; both are installed with the package.

## Next steps

- [Getting started](02-GettingStarted.md): install the package, create a document, read it and render it.
- [Creating documents](03-CreatingDocuments/01-Overview.md): pages, coordinates and the content builder.
- [Limitations](09-Limitations.md): what the library does not do.
