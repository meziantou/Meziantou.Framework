---
title: Introduction
description: Convert HTML fragments to Markdown in .NET, with options for the Markdown style, and a command-line tool.
---

`Meziantou.Framework.HtmlToMarkdown` converts HTML to Markdown: headings, paragraphs, emphasis, links, images, lists,
task lists, block quotes, code, tables and definition lists. Use it to import web pages or rich text into a Markdown
documentation, a wiki or a chat message. `Meziantou.Framework.HtmlToMarkdown.Tool` does the same from the command
line.

## What you can do with it

- **Convert a fragment** with one call: `HtmlToMarkdown.Convert(html)`. See [Getting started](02-GettingStarted.md).
- **Know what each element becomes**: blocks, inline elements, code blocks, tables, task lists and definition lists.
  See [Converted elements](03-ConvertedElements.md).
- **Choose the Markdown style**: ATX or Setext headings, `*` or `_` emphasis, fenced or indented code blocks, the list
  marker, the line breaks, smart punctuation and emoji shortcodes. See [Formatting options](04-FormattingOptions.md).
- **Decide what happens to the other elements**, and convert HTML from untrusted sources. See
  [Unknown elements and untrusted HTML](05-UnknownElementsAndUntrustedHtml.md).
- **Render the result** with [Meziantou.Framework.Markdown](../Markdown/01-Introduction.md). See
  [Rendering the Markdown](06-RenderingTheMarkdown.md).
- **Convert files from the command line**. See [Command-line tool](07-CommandLineTool.md).

## How it works

`HtmlToMarkdown.Convert` parses the HTML with the HTML parser of
[Meziantou.Framework.WebPlatform](../WebPlatform/01-Introduction.md), as a browser does: invalid markup, missing end
tags and entities are handled the same way. It then walks the tree and writes the Markdown of each element. The text
is escaped, so that a `*` or a `#` of the HTML stays a character and does not start Markdown syntax.

The output follows [CommonMark](https://commonmark.org/), and uses the common extensions for what CommonMark does not
have: pipe tables, task lists, strikethrough and definition lists.

`HtmlToMarkdown` and its options are in the [Meziantou.Framework](cref:Meziantou.Framework.HtmlToMarkdown!N:Meziantou.Framework) namespace.

## Next steps

- [Getting started](02-GettingStarted.md): install the package and convert your first fragment.
- [Converted elements](03-ConvertedElements.md): the Markdown each element produces.
- [Limitations](08-Limitations.md): what the conversion does not keep.
