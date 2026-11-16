---
title: EPUB books and comic book archives
description: Convert EPUB books to PDF documents, comic book archives or images, and comic book archives to PDF documents or images.
---

## Convert an EPUB book to a PDF document

The text of the book is laid out with its style sheets on pages of `--epub-page-size`, A5 by default. Each document of
the book (usually a chapter) starts a new page. The text of the PDF document stays text, so it can be searched and
selected, and the title and author of the book are the metadata of the document:

```bash
Meziantou.Framework.FileConversion.Tool book.epub --output-format pdf
Meziantou.Framework.FileConversion.Tool book.epub --output book.pdf --epub-page-size A4 --epub-margin 20 --epub-font-size 12
```

| Option | Description |
| --- | --- |
| `--epub-page-size` | `A3`, `A4`, `A5` (the default), `Letter`, `Legal`, or a size such as `148x210mm` |
| `--epub-margin` | The margin of the pages, in millimeters. 15 by default |
| `--epub-font-size` | The size of the text, in points. By default, the size the style sheets of the book set |
| `--epub-mode` | `Auto` (the default), `Render` or `Images`: see below |

The book is read on its own: the tool loads its style sheets, images and fonts from the book, and never from the
network. Scripts do not run.

## Books whose pages are images

The pages of a comic or a manga are images: each document of the book shows one image. With `--epub-mode Auto`, the
default, the tool detects these books and uses their images as the pages, at their own size. `--epub-mode Images`
requires such a book, and `--epub-mode Render` lays it out like a book of text.

To get a comic book archive from such a book, convert it to CBZ. The images are copied as they are:

```bash
Meziantou.Framework.FileConversion.Tool comic.epub --output-format cbz
```

## Convert an EPUB book to images

A book of text gives one image per page, rendered at `--dpi` (150 dots per inch by default). The number of pages is
only known once the book is laid out, so the numbers in the names of the files have 4 digits: `book-0001.png`,
`book-0002.png`...

```bash
Meziantou.Framework.FileConversion.Tool book.epub --output-format png --dpi 100
```

## Comic book archives

A comic book archive is an archive of images: the tool reads the images it contains, sorted by name in natural order
(`page2` before `page10`), and skips the other files.

| Format | Extensions | Container | Read | Write |
| --- | --- | --- | --- | --- |
| CBZ | `.cbz` | zip | Yes | Yes |
| CBR | `.cbr` | RAR | Yes | No |
| CB7 | `.cb7`, `.c7z` | 7z | Yes | Yes |
| CBT | `.cbt` | tar | Yes | Yes |

The extension tells that a file is a comic book archive, and its content tells the container: a `.cbz` file that is a
RAR archive, as some downloads are, is read as a CBR file. Any other archive of images, such as a `.zip` or a `.7z`
file, converts to a PDF document or images the same way.

The tool writes the images as `0001.jpg`, `0002.png`... without compression, since the images are already compressed.
`--archive-compression-level` and the options of the container (such as `--zip-compression-method`) compress them
anyway: see [Archives](07-Archives.md).

```bash
Meziantou.Framework.FileConversion.Tool "pages/*.jpg" --output comic.cbz
Meziantou.Framework.FileConversion.Tool comic.cbr --output-format pdf
Meziantou.Framework.FileConversion.Tool comic.cbr --output-format cb7
Meziantou.Framework.FileConversion.Tool document.pdf --output-format cbz --comic-image-format jpeg --jpeg-quality 85
```

Converting a comic book archive to another comic book format writes its pages again, renumbered, and leaves out the
other files, such as `ComicInfo.xml`. To keep every file as it is, convert it to an archive format (`--output-format
zip`) and rename the file.

`--comic-image-format` sets the format of the images of a comic book archive output. `Keep`, the default, copies the
JPEG, PNG, GIF and WebP images as they are and writes the other pages (such as the pages of a PDF document) as PNG.
`Jpeg`, `Png` and `WebP` write every image in that format.

A comic book archive protected by a password is read with `--archive-password`.
