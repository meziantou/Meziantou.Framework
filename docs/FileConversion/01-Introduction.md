---
title: Introduction
description: A command-line tool that converts images, PDF documents, EPUB books, Markdown and HTML documents, comic book archives and archives from one format to another.
---

`Meziantou.Framework.FileConversion.Tool` is a .NET tool that converts files from one format to another:

- an image to another image format: PNG to JPEG, JPEG to AVIF, HEIC to PNG, and so on
- a PDF document to one image per page
- images to a PDF document, a comic book archive (CBZ, CB7, CBT) or a multi-page TIFF file
- an EPUB book to a PDF document, a comic book archive or images
- a comic book archive (CBZ, CBR, CB7, CBT) to a PDF document, images or another comic book archive
- a Markdown document to an HTML page, and an HTML page to a Markdown document
- an archive to another archive format: zip, 7z, tar, .tar.gz, .tar.bz2, .tar.xz... and RAR as an input
- several PDF documents to one PDF document, and a PDF document to one document per page
- a PDF document to PDF/A, to a smaller, linearized or encrypted PDF document, or to a PDF document whose form fields are flattened

Each format has its own options, such as `--jpeg-quality`, `--dpi` or `--epub-page-size`.

## What you can do with it

- **Install the tool and convert a first file**. See [Getting started](02-GettingStarted.md).
- **Convert images**: choose the format, the quality and the size. See [Images](03-Images.md).
- **Convert PDF documents** to images, and images to PDF documents; merge and split PDF documents, and save them as
  PDF/A, smaller, linearized or encrypted. See [PDF documents](04-Pdf.md).
- **Convert EPUB books and comic book archives**. See [EPUB books and comic book archives](05-EpubAndComicBooks.md).
- **Name the output files** when an input gives several files. See [Output file names](06-OutputNames.md).
- **Convert Markdown and HTML** documents to each other. See [Markdown and HTML](08-MarkdownAndHtml.md).
- **Convert archives** from one format to another, such as RAR to zip or zip to .tar.gz. See [Archives](07-Archives.md).

The [command reference](09-CommandReference.md) lists every option.

## How it works

The tool reads each input as a sequence of pages, then writes the pages in the output format.

- An image file has one page, and a multi-page TIFF file has one page per image.
- A PDF document has its pages.
- A comic book archive, or any other archive, has its images, sorted by name.
- An EPUB book has the pages its text is laid out on, or its images when its pages are images (comics).

The format of the output decides how many files the tool writes:

- **PDF, comic book archives and TIFF** hold several pages: the pages of all the inputs go to one file, or to one file
  per input with `--per-input`, or to one file per page with `--per-page`.
- **The other image formats** hold one image: each page gives one file.
- **Archives** (zip, 7z, tar...) are not made of pages: each input archive gives one archive, with the same files.

The tool detects the format of an input from its content, so a file with a wrong extension is still read. The PDF
pages are copied into a PDF output as they are, so their text stays text; they are rendered to images for the other
outputs. The JPEG files are copied into a PDF or comic book output without being decoded, so they keep their quality.
