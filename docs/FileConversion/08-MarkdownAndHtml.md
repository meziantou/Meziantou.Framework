---
title: Markdown and HTML
description: Convert Markdown documents to HTML pages, and HTML pages to Markdown documents.
---

The tool converts a Markdown document to HTML, and an HTML document to Markdown. Each input gives one output, next to
it by default:

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
`<!DOCTYPE html>` or `<html>`. The files are read as UTF-8, unless they start with a byte order mark, and written as
UTF-8. These documents are not converted to other formats, such as PDF or images.

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
