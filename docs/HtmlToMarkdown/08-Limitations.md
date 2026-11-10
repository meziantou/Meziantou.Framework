---
title: Limitations
description: What the conversion does not keep, and the HTML that Markdown cannot write.
---

Markdown writes less than HTML. The conversion keeps the structure and the text of the
[converted elements](03-ConvertedElements.md), and loses the rest:

- **Attributes and styles.** Only `href` and `title` of links, `src`, `alt` and `title` of images, the `start` of
  ordered lists, the `checked` state of checkboxes, the alignment and the spans of table cells, the language of code
  blocks and the `white-space` declarations of `style` attributes are read. The `class`, `id`, `data-*` attributes and
  the other styles are lost, except on the elements written as raw HTML.
- **Fragments.** [Convert](cref:Meziantou.Framework.HtmlToMarkdown.Convert) takes the content of a `body`. A complete page gives its `head` elements as raw HTML: see
  [Convert a complete document](02-GettingStarted.md#convert-a-complete-document).
- **Whitespace.** The whitespace of the text is collapsed, as a browser displays it, unless a `style` attribute
  preserves it: see [Preserved whitespace](03-ConvertedElements.md#preserved-whitespace). Style sheets are not read: an
  element that a `class` styles with `white-space: pre` is collapsed.
- **Code.** A code block or a code span keeps the text of its content, without its elements.
- **Tables.** A pipe table has one header row, and one line per row. By default, a cell takes one column, the shorter
  rows are completed with empty cells, and the lists, code blocks and other blocks of a cell are flattened onto one
  line, separated by `<br>`. [TableSpanHandling](03-ConvertedElements.md#spanned-cells) keeps the columns of spanned
  cells, and [ComplexTableHandling](03-ConvertedElements.md#complex-tables) writes the other tables as raw HTML.
- **Forms and media.** `input` elements other than checkboxes are removed. `sub`, `sup`, `u`, `kbd`, `video`, `audio`
  and `iframe` are unknown elements unless [their option](03-ConvertedElements.md#subscript-superscript-underline-and-keyboard)
  chooses a syntax. The other elements without a Markdown syntax are unknown elements: raw HTML, removed or unwrapped,
  depending on [`UnknownElementHandling`](05-UnknownElementsAndUntrustedHtml.md).
- **Security.** The converter is not a sanitizer: see
  [Convert untrusted HTML](05-UnknownElementsAndUntrustedHtml.md#convert-untrusted-html).

The escapes are conservative: the Markdown can hold a backslash that is not needed, such as `1\.20` at the start of a
table cell. It renders the same text.
