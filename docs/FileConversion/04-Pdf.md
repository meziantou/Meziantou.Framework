---
title: PDF documents
description: Render the pages of a PDF document to images, place images on the pages of a PDF document, merge and split PDF documents, and save them as PDF/A, smaller, linearized or encrypted.
---

## Convert a PDF document to images

Each page gives one image. `--dpi` sets the resolution, 150 dots per inch by default:

```bash
Meziantou.Framework.FileConversion.Tool report.pdf --output-format png --dpi 200
```

This writes `report-1.png`, `report-2.png`... next to the document, numbered with as many digits as the number of
pages so that they sort in order. [Output file names](06-OutputNames.md) shows how to change the names.

`--pages` selects the pages, for every input: `--pages 1` for the first page, `--pages 1-3,7,10-` for the pages 1 to 3,
7, and from 10 to the end.

| Option | Description |
| --- | --- |
| `--dpi` | The resolution of the images, in dots per inch. 150 by default |
| `--pdf-password` | The password of an encrypted document |
| `--pdf-render-annotations` | Render the annotations (form fields, stamps, comments). `true` by default |

## Convert images to a PDF document

The images of all the inputs go to one document, one image per page, in the order of the inputs:

```bash
Meziantou.Framework.FileConversion.Tool cover.jpg "pages/*.png" --output book.pdf
```

By default, each page has the size of its image, at the resolution the image declares (96 dots per inch when it
declares none). `--page-size` places the images on pages of a fixed size, scaled to fit inside the margins and
centered:

```bash
Meziantou.Framework.FileConversion.Tool "scans/*.jpg" --output scans.pdf --page-size A4 --page-margin 10
```

| Option | Description |
| --- | --- |
| `--page-size` | `fit` (the size of each image, the default), `A3`, `A4`, `A5`, `Letter`, `Legal`, or a size such as `210x297mm`, `8.5x11in` or `595x842pt` |
| `--page-margin` | The margin around the images, in millimeters. 0 by default |
| `--page-orientation` | `Auto` (the default) turns the page of a landscape image; `Portrait` and `Landscape` keep one orientation |
| `--pdf-image-encoding` | `Auto` (the default) copies the JPEG files as they are and compresses the other images without loss; `Jpeg` compresses every image as JPEG (with `--jpeg-quality`); `Lossless` compresses every image without loss |

To write one document per image, rather than one document for all of them, use `--per-input`:

```bash
Meziantou.Framework.FileConversion.Tool "scans/*.jpg" --output-format pdf --per-input
```

## Merge PDF documents

The pages of PDF documents are copied as they are, with their text, their fonts and their links:

```bash
Meziantou.Framework.FileConversion.Tool part1.pdf part2.pdf --output merged.pdf
Meziantou.Framework.FileConversion.Tool report.pdf --output summary.pdf --pages 1-2
```

The structure of a tagged document is not kept, and neither are the form fields of the copied pages: to keep the values
of a filled-in form, `--pdf-flatten-forms` draws the fields in the content of their pages first. The values can then no
longer be changed.

```bash
Meziantou.Framework.FileConversion.Tool filled-form.pdf --output form-copy.pdf --pdf-flatten-forms
```

## Split a PDF document

`--per-page` writes each page to its own document, named like the images of a PDF document (`report-1.pdf`,
`report-2.pdf`...). It works for every input, and for comic book archives and TIFF files too:

```bash
Meziantou.Framework.FileConversion.Tool report.pdf --output-format pdf --per-page
Meziantou.Framework.FileConversion.Tool report.pdf --output "chapters/{page:00}.pdf" --per-page --pages 3-
```

## Options of the PDF output

These options apply to every PDF output: images, EPUB books and comic book archives converted to PDF, and PDF documents
merged, split or copied. To change a PDF document, convert it to a new PDF file.

### Save as PDF/A

PDF/A is the PDF format for archiving: the document embeds what it needs, such as its fonts, so that it looks the same
in every reader in the long term. `--pdf-conformance` sets the level:

```bash
Meziantou.Framework.FileConversion.Tool "scans/*.jpg" --output archive.pdf --pdf-conformance PdfA2B
Meziantou.Framework.FileConversion.Tool report.pdf --output report-archive.pdf --pdf-conformance PdfA2U
```

| Level | Description |
| --- | --- |
| `PdfA2B` | PDF/A-2b: the pages look the same in every reader. The usual choice |
| `PdfA2U` | PDF/A-2u: PDF/A-2b, and the text can be extracted as Unicode |
| `PdfA3B` | PDF/A-3b: PDF/A-2b, and the document can embed files of any format |
| `PdfA3U` | PDF/A-3u: PDF/A-3b, and the text can be extracted as Unicode |

The conversion fixes what it can: it removes the JavaScript and the actions PDF/A does not allow, embeds substitute
fonts for the standard fonts that are not embedded, and rebuilds the metadata. When a requirement cannot be met, the tool
lists it and writes no file. The colors are declared as sRGB; a document whose colors are CMYK needs the ICC profile of
its printing condition, set with `--pdf-output-intent`. The tool checks the requirements it knows: to be sure that a
file conforms, check it with a validator such as [veraPDF](https://verapdf.org/).

PDF/A does not allow encryption, so `--pdf-conformance` cannot be used with `--pdf-output-password` and
`--pdf-owner-password`.

### Make the file smaller

`--pdf-max-image-dpi` resamples the images whose resolution on the page is higher than this one. It makes a scanned
document much smaller. The JPEG images are compressed as JPEG again, the other images without loss:

```bash
Meziantou.Framework.FileConversion.Tool scan.pdf --output scan-small.pdf --pdf-max-image-dpi 150 --pdf-optimize
```

`--pdf-optimize` also removes the resources that the pages do not use and the page thumbnails.

### Fast web view

`--pdf-linearize` writes the pages in order, with an index that lets a viewer show the first pages of a document before
it is fully downloaded. A linearized file cannot be encrypted.

### Encrypt the document

`--pdf-output-password` encrypts the document (AES-256) with a password that is needed to open it. `--pdf-owner-password`
sets a second password, which allows every operation; with it, `--pdf-permissions` lists the operations that the first
password allows:

```bash
Meziantou.Framework.FileConversion.Tool report.pdf --output protected.pdf --pdf-output-password secret
Meziantou.Framework.FileConversion.Tool report.pdf --output protected.pdf --pdf-owner-password owner-secret --pdf-permissions Print,CopyContent
```

Without `--pdf-output-password`, the document opens without a password, and readers allow only the `--pdf-permissions`
unless the owner password is given. The permissions are `None`, `Print`, `PrintHighQuality`, `Modify`, `CopyContent`,
`Annotate`, `FillForms`, `ExtractForAccessibility`, `Assemble` and `All`, separated by commas. Readers enforce them, not
the encryption: they do not protect the content against a tool that ignores them.

To remove the password of a document, open it with `--pdf-password`: the output is not encrypted.

```bash
Meziantou.Framework.FileConversion.Tool protected.pdf --output report.pdf --pdf-password secret
```

| Option | Description |
| --- | --- |
| `--pdf-conformance` | `None` (the default), `PdfA2B`, `PdfA2U`, `PdfA3B` or `PdfA3U` |
| `--pdf-output-intent` | The ICC profile of the output intent of a PDF/A output. sRGB by default |
| `--pdf-max-image-dpi` | Resample the images above this resolution, in dots per inch |
| `--pdf-optimize` | Remove the unused resources and the page thumbnails |
| `--pdf-linearize` | Linearize the file for fast web view |
| `--pdf-output-password` | Encrypt the document with the password needed to open it |
| `--pdf-owner-password` | Encrypt the document with the password that allows every operation |
| `--pdf-permissions` | The operations allowed without the owner password. Needs `--pdf-owner-password` |
| `--pdf-flatten-forms` | Draw the form fields of the PDF inputs in their pages, so that their values are kept |
