# Meziantou.Framework.FileConversion.Tool

`Meziantou.Framework.FileConversion.Tool` converts files from one format to another: images (PNG, JPEG, WebP, AVIF,
HEIC, JPEG XL, TIFF, GIF...), SVG drawings, PDF documents, EPUB and Kindle (MOBI, AZW3) books, Markdown and HTML documents, comic book archives (CBZ, CBR, CB7, CBT), archives (zip,
7z, RAR, tar, .tar.gz, .tar.bz2, .tar.xz...), mailboxes (PST, OST, mbox, Maildir) and email messages (EML, MSG, OFT, EMLX,
winmail.dat).

- an image to another image format
- an SVG drawing to an image, or to a PDF document whose drawing stays vector graphics
- a PDF document to one image per page, and images to a PDF document
- an EPUB or Kindle book (MOBI, AZW3) to a PDF document, a comic book archive or images
- a comic book archive to a PDF document, images or another comic book archive, and images to a comic book archive
- a Markdown document to an HTML page, and an HTML page to a Markdown document
- a Markdown or HTML document to a PDF document or images
- an archive to another archive format, such as RAR to zip or zip to .tar.gz
- several PDF documents to one PDF document, and a PDF document to one document per page
- a PDF document to PDF/A, or to a smaller, linearized or encrypted PDF document
- a mailbox to another mailbox format with its folders, such as PST to mbox (Thunderbird) or Maildir to PST
- a mailbox to a folder of .eml or .msg files, and .eml or .msg files to one mailbox, such as one mbox file
- an email message to another message format, such as MSG to EML or winmail.dat to MSG

## Install

```bash
dotnet tool install --global Meziantou.Framework.FileConversion.Tool
```

## Examples

Convert an image, next to the input file:

```bash
Meziantou.Framework.FileConversion.Tool photo.png --output-format jpg --jpeg-quality 85
```

Render an SVG drawing to a PNG image 512 pixels wide, or to a vector PDF document:

```bash
Meziantou.Framework.FileConversion.Tool logo.svg --output-format png --svg-width 512
Meziantou.Framework.FileConversion.Tool logo.svg --output-format pdf
```

Convert the pages of a PDF document to images, named with a template:

```bash
Meziantou.Framework.FileConversion.Tool report.pdf --output "images/page-{page:000}.png" --dpi 200
```

Convert images to one PDF document, on A4 pages:

```bash
Meziantou.Framework.FileConversion.Tool "scans/*.jpg" --output scans.pdf --page-size A4 --page-margin 10
```

Save a PDF document as PDF/A, or split it into one document per page:

```bash
Meziantou.Framework.FileConversion.Tool report.pdf --output report-archive.pdf --pdf-conformance PdfA2B
Meziantou.Framework.FileConversion.Tool report.pdf --output-format pdf --per-page
```

Convert an EPUB or Kindle book to a PDF document, or a comic to a comic book archive:

```bash
Meziantou.Framework.FileConversion.Tool book.epub --output book.pdf --layout-page-size A5
Meziantou.Framework.FileConversion.Tool comic.epub --output-format cbz
Meziantou.Framework.FileConversion.Tool book.azw3 --output-format pdf
```

Convert a comic book archive, or an archive to another archive format:

```bash
Meziantou.Framework.FileConversion.Tool comic.cbr --output-format pdf
Meziantou.Framework.FileConversion.Tool backup.rar --output-format tar.gz
```

Convert a Markdown document to an HTML page or a PDF document, or an HTML page to Markdown:

```bash
Meziantou.Framework.FileConversion.Tool readme.md --output-format pdf
Meziantou.Framework.FileConversion.Tool readme.md --output-format html --html-stylesheet site.css
Meziantou.Framework.FileConversion.Tool page.html --output-format md
```

Convert a mailbox to another format with its folders, export it to .eml files, convert an email message, or gather .eml
files in one mbox file:

```bash
Meziantou.Framework.FileConversion.Tool archive.pst --output-format mbox
Meziantou.Framework.FileConversion.Tool ~/Maildir --output mail.pst
Meziantou.Framework.FileConversion.Tool archive.pst --output-format eml
Meziantou.Framework.FileConversion.Tool message.msg --output-format eml
Meziantou.Framework.FileConversion.Tool "inbox/*.eml" --output inbox.mbox
```

List the formats and their options:

```bash
Meziantou.Framework.FileConversion.Tool --list-formats
```

<!-- help -->
## Help

```
Description:
  Convert files from one format to another: images (PNG, JPEG, WebP, AVIF, HEIC, JPEG XL, TIFF, GIF...), PDF, SVG, EPUB, MOBI, AZW3, Markdown, HTML, comic book archives (CBZ, CBR, CB7, CBT), archives (zip, 7z, RAR, tar...), mailboxes (PST, OST, mbox, Maildir) and email messages (EML, MSG, EMLX, winmail.dat)

Usage:
  Meziantou.Framework.FileConversion.Tool [<inputs>...] [options]

Arguments:
  <inputs>  The files to convert, or glob patterns such as 'scans/*.jpg'. The files a pattern matches are sorted in natural order (page2 before page10)

Options:
  -o, --output <output>                                                                                                       The path of the output files, with the placeholders {name}, {ext}, {dir}, {page}, {index} and {count}. {page:000} pads the number with zeros. Default: '{dir}/{name}.{ext}', or '{dir}/{name}-{page}.{ext}' when an input gives several images
  --input-format <input-format>                                                                                               The format of the inputs, when it must not be detected from their content
  --output-format <output-format>                                                                                             The format of the outputs. Default: the format of the extension of --output
  --overwrite                                                                                                                 Replace the output files that already exist
  --per-input                                                                                                                 Convert each input to its own PDF, comic book archive, TIFF file or mailbox, instead of all the inputs to one file
  --per-page                                                                                                                  Write each page to its own PDF, comic book archive or TIFF file, such as to split a PDF file. The output template needs {page} or {index}
  --pages <pages>                                                                                                             The pages of each input to convert, such as '1-3,7,10-'. Default: all the pages
  --list-formats                                                                                                              List the formats, their extensions and their options
  --max-width <max-width>                                                                                                     Shrink the images to this width, in pixels, keeping their aspect ratio
  --max-height <max-height>                                                                                                   Shrink the images to this height, in pixels, keeping their aspect ratio
  --dpi <dpi>                                                                                                                 The resolution of the images of the PDF, EPUB and MOBI pages, in dots per inch [default: 150]
  --svg-width <svg-width>                                                                                                     The width of the images of the SVG documents, in pixels. With only --svg-width, the height keeps the aspect ratio. Default: the width of the document, one pixel per CSS pixel
  --svg-height <svg-height>                                                                                                   The height of the images of the SVG documents, in pixels. With --svg-width too, the image fits in both, keeping the aspect ratio. Default: the height of the document
  --background <background>                                                                                                   The color the transparent pixels are composited over in JPEG output, in hexadecimal notation [default: #ffffff]
  --jpeg-quality <jpeg-quality>                                                                                               The quality of the JPEG images, from 1 to 100. Default: 90
  --webp-quality <webp-quality>                                                                                               The quality of the lossy WebP images, from 0 to 100
  --webp-lossless                                                                                                             Compress the WebP images without loss
  --avif-quality <avif-quality>                                                                                               The quality of the lossy AVIF images, from 0 to 100
  --avif-effort <avif-effort>                                                                                                 The compression effort of the AVIF images, from 0 (fastest) to 9 (smallest)
  --avif-lossless                                                                                                             Compress the AVIF images without loss
  --heic-quality <heic-quality>                                                                                               The quality of the lossy HEIC images, from 0 to 100
  --jxl-quality <jxl-quality>                                                                                                 The quality of the lossy JPEG XL images, from 0 to 100
  --jxl-lossless                                                                                                              Compress the JPEG XL images without loss
  --png-compression-level <Fastest|NoCompression|Optimal|SmallestSize>                                                        The compression level of the PNG images
  --tiff-compression <CcittGroup3|CcittGroup4|CcittModifiedHuffman|Deflate|Jpeg|Lzw|None|PackBits>                            The compression of the TIFF images
  --gif-max-colors <gif-max-colors>                                                                                           The maximum number of colors of the GIF images, from 2 to 256
  --pdf-password <pdf-password>                                                                                               The password of the encrypted PDF inputs
  --pdf-render-annotations                                                                                                    Render the annotations of the PDF pages (form fields, stamps, comments) in the images. Use '--pdf-render-annotations false' to leave them out [default: true]
  --page-size <page-size>                                                                                                     The size of the pages of the images in a PDF output: 'fit' (the size of each image), A3, A4, A5, Letter, Legal, or a size such as 210x297mm, 8.5x11in or 595x842pt [default: fit]
  --page-margin <page-margin>                                                                                                 The margin around the images in a PDF output, in millimeters [default: 0]
  --page-orientation <Auto|Landscape|Portrait>                                                                                The orientation of the pages of a fixed --page-size. Auto turns the page of a landscape image [default: Auto]
  --pdf-image-encoding <Auto|Jpeg|Lossless>                                                                                   The compression of the images in a PDF output. Auto embeds the JPEG files as they are, and compresses the other images without loss [default: Auto]
  --pdf-conformance <None|PdfA2B|PdfA2U|PdfA3B|PdfA3U>                                                                        Save the PDF output as PDF/A, for archiving. PdfA2B is the usual choice; the U levels also make the text extractable as Unicode, and PDF/A-3 allows embedded files of any format [default: None]
  --pdf-output-intent <pdf-output-intent>                                                                                     The ICC profile of the output intent of a PDF/A output, such as the CMYK profile of its printing condition. Default: sRGB
  --pdf-max-image-dpi <pdf-max-image-dpi>                                                                                     Resample the images of the PDF output whose resolution is above this one, in dots per inch, to make the file smaller
  --pdf-optimize                                                                                                              Remove the resources the pages do not use and the page thumbnails from the PDF output
  --pdf-linearize                                                                                                             Linearize the PDF output (fast web view): a viewer shows the first pages before the file is downloaded
  --pdf-output-password <pdf-output-password>                                                                                 Encrypt the PDF output (AES-256) with this password, which is needed to open it
  --pdf-owner-password <pdf-owner-password>                                                                                   Encrypt the PDF output (AES-256) with this owner password, which allows every operation. Without --pdf-output-password, the file opens without a password, with the --pdf-permissions
  --pdf-permissions <All|Annotate|Assemble|CopyContent|ExtractForAccessibility|FillForms|Modify|None|Print|PrintHighQuality>  The operations allowed without the owner password, separated by commas: None, Print, PrintHighQuality, Modify, CopyContent, Annotate, FillForms, ExtractForAccessibility, Assemble, All. Needs --pdf-owner-password. Default: All
  --pdf-flatten-forms                                                                                                         Draw the form fields of the PDF inputs in the content of their pages, so that their values are kept in the PDF output. Without it, the PDF output leaves the form fields out
  --epub-mode <Auto|Images|Render>                                                                                            How the EPUB pages are made. Auto uses the images of a book whose pages are images (comics), and lays out the text of the other books [default: Auto]
  --layout-page-size <layout-page-size>                                                                                       The size of the pages the text of EPUB, MOBI, Markdown and HTML documents is laid out on: A3, A4, A5, Letter, Legal, or a size such as 148x210mm. Default: A5 for EPUB and MOBI, A4 for Markdown and HTML
  --layout-margin <layout-margin>                                                                                             The margin of the pages the text of EPUB, MOBI, Markdown and HTML documents is laid out on, in millimeters [default: 15]
  --layout-font-size <layout-font-size>                                                                                       The size of the text of EPUB, MOBI, Markdown and HTML documents laid out on pages, in points. Default: the size of the style sheets of the document
  --html-root <html-root>                                                                                                     The folder whose files (images, style sheets, fonts) Markdown and HTML documents laid out on pages can load, such as the root of a documentation site. Default: the folder of each document. Nothing is loaded from the network
  --markdown-extensions <markdown-extensions>                                                                                 The Markdown extensions of a Markdown input, separated by '+': 'common' for CommonMark only, 'advanced' (tables, task lists, footnotes, alerts...), or extensions such as 'pipetables+tasklists+emojis' [default: advanced]
  --markdown-disable-html                                                                                                     Write the raw HTML of a Markdown input as text, for a document from an untrusted source
  --html-fragment                                                                                                             Write only the content of the body of the HTML output, without <html>, <head> and <body>
  --html-title <html-title>                                                                                                   The title of the HTML output. Default: the title of the front matter of the Markdown document, its first heading, or its file name
  --html-stylesheet <html-stylesheet>                                                                                         The URL of a style sheet the HTML output links to. Repeat the option for several style sheets
  --markdown-heading-style <Atx|Setext>                                                                                       The style of the headings of the Markdown output [default: Atx]
  --markdown-code-block-style <Fenced|Indented>                                                                               The style of the code blocks of the Markdown output [default: Fenced]
  --markdown-emphasis-marker <Asterisk|Underscore>                                                                            The marker of the emphasis of the Markdown output [default: Asterisk]
  --markdown-list-marker <markdown-list-marker>                                                                               The marker of the unordered lists of the Markdown output: '-', '*' or '+' [default: -]
  --markdown-unknown-elements <PassThrough|Strip|StripKeepContent>                                                            What the Markdown output does with the HTML elements Markdown cannot represent [default: PassThrough]
  --markdown-complex-tables <Flatten|Html>                                                                                    What the Markdown output does with the tables a pipe table cannot represent [default: Flatten]
  --comic-image-format <Jpeg|Keep|Png|WebP>                                                                                   The format of the images of a comic book archive output (CBZ, CB7, CBT). Keep copies the JPEG, PNG, GIF and WebP images as they are, and writes the other pages as PNG [default: Keep]
  --archive-password <archive-password>                                                                                       The password of the encrypted archive inputs (zip, 7z, RAR)
  --archive-compression-level <Fastest|NoCompression|Optimal|SmallestSize>                                                    The compression level of an archive output. Default: Optimal, and no compression for the images of a comic book archive
  --zip-compression-method <BZip2|Deflate|Lzma|Ppmd|Stored|Xz|Zstandard>                                                      The compression method of the entries of a zip output. Default: Deflate. Zstandard needs .NET 11
  --zip-password <zip-password>                                                                                               Encrypt the entries of a zip output with this password
  --zip-encryption <Aes128|Aes192|Aes256|ZipCrypto>                                                                           The encryption of the entries of a zip output, with --zip-password. ZipCrypto is weak, but older tools read only this one [default: Aes256]
  --7z-compression-method <BZip2|Copy|Deflate|Lzma|Lzma2|Ppmd>                                                                The compression method of a 7z output. Default: Lzma2
  --7z-solid                                                                                                                  Compress the files of a 7z output together, which makes it smaller. Use '--7z-solid false' to compress each file on its own [default: true]
  --7z-password <7z-password>                                                                                                 Encrypt the content of a 7z output with this password (AES-256)
  --7z-encrypt-headers                                                                                                        Also encrypt the names of the files of a 7z output, with --7z-password
  --mbox-format <Mboxcl|Mboxcl2|Mboxo|Mboxrd>                                                                                 The variant of the mbox files of an mbox input or output: how the lines of the messages that start with 'From ' are escaped. Mboxrd is the format of most mail tools [default: Mboxrd]
  --email-include-non-mail-items                                                                                              Write the items of a PST or OST input that are not emails (contacts, appointments, tasks, notes...) as messages in an mbox, Maildir or EML output. Without it, they are skipped. A PST, OST or MSG output keeps them as they are
  --pst-format <Ansi|Unicode|Unicode4K>                                                                                       The format of a PST output: Unicode (Outlook 2003 and later) or Ansi (Outlook 97 to 2002, files up to 2 GB). Default: Unicode. An OST output has the Unicode4K format
  --pst-encryption <Cyclic|None|Permute>                                                                                      How the data of a PST or OST output is obfuscated: None, Permute (the default of Outlook) or Cyclic. None of them needs a password, so they do not protect the data [default: Permute]
  -?, -h, --help                                                                                                              Show help and usage information
  --version                                                                                                                   Show version information
```
<!-- help -->