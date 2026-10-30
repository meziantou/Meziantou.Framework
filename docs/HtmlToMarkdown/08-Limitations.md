---
title: Limitations
description: What the conversion does not keep, and the HTML that Markdown cannot write.
---

Markdown writes less than HTML. The conversion keeps the structure and the text of the
[converted elements](03-ConvertedElements.md), and loses the rest:

- **Attributes and styles.** Only `href` and `title` of links, `src`, `alt` and `title` of images, the `start` of
  ordered lists, the `checked` state of checkboxes, the alignment of table cells and the language of code blocks are
  read. The `class`, `id`, `style` and `data-*` attributes are lost, except on unknown elements written as raw HTML.
- **Fragments.** [Convert](cref:Meziantou.Framework.HtmlToMarkdown.Convert) takes the content of a `body`. A complete page gives its `head` elements as raw HTML: see
  [Convert a complete document](02-GettingStarted.md#convert-a-complete-document).
- **Whitespace.** The whitespace of the text is collapsed, as a browser displays it. Only code blocks keep their line
  breaks and their indentation. CSS is not read: an element styled with `white-space: pre` is collapsed too.
- **Code.** A code block or a code span keeps the text of its content, without its elements.
- **Tables.** A pipe table has one header row, and one line per row. `colspan` and `rowspan` are ignored: a cell takes
  one column, and the shorter rows are completed with empty cells. Lists, code blocks and other blocks of a cell are
  flattened onto one line, separated by `<br>`.
- **Forms and media.** `input` elements other than checkboxes are removed. `video`, `audio`, `iframe`, `sub`, `sup`,
  `u`, `kbd` and the other elements without a Markdown syntax are unknown elements: raw HTML, removed or unwrapped,
  depending on [`UnknownElementHandling`](05-UnknownElementsAndUntrustedHtml.md).
- **Security.** The converter is not a sanitizer: see
  [Convert untrusted HTML](05-UnknownElementsAndUntrustedHtml.md#convert-untrusted-html).

The escapes are conservative: the Markdown can hold a backslash that is not needed, such as `1\.20` at the start of a
table cell. It renders the same text.
