---
title: Markdown and HTML
description: Convert Markdown documents to HTML pages, HTML pages to Markdown documents, and both to PDF documents or images.
---

The tool converts a Markdown document to HTML, and an HTML document to Markdown. It also lays out both on pages, to a
PDF document or images. Each input gives one output, next to it by default:

```bash
Meziantou.Framework.FileConversion.Tool readme.md --output-format html
Meziantou.Framework.FileConversion.Tool "docs/*.md" --output "site/{name}.html"
Meziantou.Framework.FileConversion.Tool page.html --output-format md
```

| Format | Extensions | Read | Write |
| --- | --- | --- | --- |
| Markdown | `.md`, `.markdown` | Yes | Yes |
| HTML | `.html`, `.htm` | Yes | Yes |

A Markdown or HTML file is recognized by its extension; a file without one is read as HTML when it starts with
`<!DOCTYPE html>` or `<html>`. A Markdown file is read as UTF-8, unless it starts with a byte order mark; the outputs are
written as UTF-8.

## Markdown to HTML

The output is a complete HTML page, whose title is the `title` of the YAML front matter of the document, its first
heading, or its file name. `--html-stylesheet` links the page to style sheets; repeat it for several style sheets:

```bash
Meziantou.Framework.FileConversion.Tool guide.md --output guide.html --html-stylesheet ../styles/site.css
```

`--html-fragment` writes only the HTML of the content, without `<html>`, `<head>` and `<body>`, to insert it in
another page.

The Markdown is read as [CommonMark](https://commonmark.org/), with the extensions of `--markdown-extensions`.
`advanced`, the default, enables the tables, the task lists, the footnotes, the alerts (`> [!NOTE]`), the
definition lists, the abbreviations, the figures, the mathematics, the diagrams, the automatic links and the
identifiers of the headings. Combine extensions with `+`, such as `common+pipetables+tasklists` or `advanced+emojis`:

| Extension | Description |
| --- | --- |
| `common` | CommonMark only |
| `advanced` | The extensions listed above |
| `pipetables`, `gridtables` | Tables |
| `tasklists` | `- [x]` check boxes |
| `footnotes` | `[^1]` notes |
| `alerts` | GitHub alerts: `> [!NOTE]`, `> [!WARNING]`... |
| `emojis` | `:smile:` emojis |
| `hardlinebreak` | A line break in a paragraph is a line break in the page |
| `autoidentifiers` | Identifiers on the headings, for the links to them |
| `smartypants` | Typographic quotes and dashes |

The YAML front matter of a document is never written to the page.

The raw HTML of a Markdown document is copied to the page as it is. For a document from an untrusted source, use
`--markdown-disable-html` to write it as text. Links are not filtered: sanitize the page before you show it, for
example with [HtmlSanitizer](../HtmlSanitizer/01-Introduction.md).

| Option | Description |
| --- | --- |
| `--markdown-extensions` | The Markdown extensions, separated by `+`. `advanced` by default |
| `--markdown-disable-html` | Write the raw HTML of the document as text |
| `--html-fragment` | Write only the content, without `<html>`, `<head>` and `<body>` |
| `--html-title` | The title of the page |
| `--html-stylesheet` | The URL of a style sheet the page links to. Repeat it for several style sheets |

## Markdown and HTML to PDF

The document is laid out with its style sheets on A4 pages, like a browser prints a page, and the text of the PDF
document stays text, so it can be searched and selected. A Markdown document is converted to HTML first, with the
options above: `--html-stylesheet` styles its pages. The other page outputs, such as PNG, JPEG, TIFF or CBZ, get images
of the pages:

```bash
Meziantou.Framework.FileConversion.Tool report.md --output-format pdf
Meziantou.Framework.FileConversion.Tool "docs/*.md" --output manual.pdf --layout-page-size Letter --html-stylesheet print.css
Meziantou.Framework.FileConversion.Tool page.html --output "page-{page}.png" --dpi 96
```

The pages of all the inputs go to one PDF document, or to one document per input with `--per-input`, as for the
other inputs. The title of the PDF document is the title of the page: `<title>` for an HTML document, the title of the
front matter or the first heading for a Markdown document.

The document loads the images, style sheets and fonts of its folder and of the folders below it, and nothing from the
network: an image whose URL starts with `https://` is not shown. When the document uses files of a parent folder, such
as `../images/logo.png` in a documentation site, set that folder with `--html-root`. Scripts do not run. An HTML
document is decoded as a browser does: from its byte order mark, its `<meta charset>`, or as UTF-8.

| Option | Description |
| --- | --- |
| `--layout-page-size` | `A3`, `A4` (the default), `A5`, `Letter`, `Legal`, or a size such as `210x297mm` |
| `--layout-margin` | The margin of the pages, in millimeters. 15 by default |
| `--layout-font-size` | The size of the text, in points. By default, the size the style sheets set |
| `--html-root` | The folder whose files the document can load. By default, the folder of the document |
| `--dpi` | The resolution of the images of the pages, for an image output. 150 by default |

## HTML to Markdown

The content of the `<body>` of the page is converted; its head, such as the title and the scripts, is not. The
headings, paragraphs, emphasis, links, images, lists, tables and code blocks become Markdown:

```bash
Meziantou.Framework.FileConversion.Tool article.html --output article.md --markdown-heading-style Setext
```

| Option | Description |
| --- | --- |
| `--markdown-heading-style` | `Atx` (`# Heading`, the default) or `Setext` (underlined headings) |
| `--markdown-code-block-style` | `Fenced` (the default) or `Indented` |
| `--markdown-emphasis-marker` | The marker of the emphasis: `Asterisk` (the default) or `Underscore` |
| `--markdown-list-marker` | The marker of the unordered lists: `-` (the default), `*` or `+` |
| `--markdown-unknown-elements` | What to do with the elements Markdown cannot represent, such as `<video>` |
| `--markdown-complex-tables` | What to do with the tables a Markdown table cannot represent, such as nested tables |

The [HtmlToMarkdown command-line tool](../HtmlToMarkdown/07-CommandLineTool.md) has more options, and reads from the
standard input.
