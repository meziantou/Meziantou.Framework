---
title: Introduction
description: A CommonMark compliant, extensible Markdown processor for .NET, with a syntax tree, HTML rendering and roundtrip.
---

`Meziantou.Framework.Markdown` parses Markdown and converts it to HTML in .NET. It follows the
[CommonMark](https://commonmark.org/) specification, and its extensions add the syntaxes that CommonMark does not define,
such as tables, alerts, footnotes and task lists. The library is derived from [Markdig](https://github.com/xoofx/markdig).

## What you can do with it

- **Convert Markdown to HTML** with one call, or with a pipeline that enables the extensions you need. See
  [Converting to HTML](03-ConvertingToHtml.md).
- **Enable extensions**: pipe and grid tables, GitHub alerts, footnotes, task lists, heading identifiers, automatic
  links, emojis, mathematics, diagrams, YAML front matter and more. See [Extensions](04-Extensions/01-Overview.md).
- **Inspect a document**: parse it into a syntax tree of blocks and inlines, find the headings or the links, and map each
  element back to its position in the source. See [The syntax tree](05-SyntaxTree.md) and
  [Source positions](06-SourcePositions.md).
- **Edit a document**: change the tree and render it again, or write it back as Markdown without changing its formatting.
  See [Editing documents](07-EditingDocuments.md).
- **Produce other formats**: plain text, normalized Markdown, or a format of your own. See
  [Plain text and other formats](08-PlainTextAndOtherFormats.md).
- **Extend the syntax**: process the tree after parsing, change how an element is rendered, or add your own syntax. See
  [Writing extensions](09-WritingExtensions.md).

## How it works

A conversion has two steps:

1. A **parser** reads the Markdown text and builds a `MarkdownDocument`: a tree of blocks (headings, paragraphs, lists,
   tables...) whose leaves contain inlines (text, emphasis, links, code...).
2. A **renderer** walks the tree and writes the output: HTML, plain text, normalized Markdown or the original Markdown.

A `MarkdownPipeline` holds the parsers and the renderer settings, and the extensions it enables. You build it once with
a `MarkdownPipelineBuilder`, and pass it to every call. Parse and render a document with the same pipeline: an extension
adds both the parser of its syntax and the renderer of its elements.

## Namespaces

| Namespace | Contains |
| --- | --- |
| `Meziantou.Framework.Markdown` | `MarkdownConverter`, the pipeline and its builder, the extension methods (`UsePipeTables`, …) |
| `Meziantou.Framework.Markdown.Syntax` | The document and the blocks (`HeadingBlock`, `ParagraphBlock`, `ListBlock`, …), positions |
| `Meziantou.Framework.Markdown.Syntax.Inlines` | The inlines (`LiteralInline`, `EmphasisInline`, `LinkInline`, …) |
| `Meziantou.Framework.Markdown.Renderers` | `HtmlRenderer` and the base classes of the renderers |
| `Meziantou.Framework.Markdown.Renderers.Html` | The HTML renderer of each element, and `HtmlAttributes` |
| `Meziantou.Framework.Markdown.Renderers.Normalize` | `NormalizeRenderer` and `NormalizeOptions` |
| `Meziantou.Framework.Markdown.Renderers.Roundtrip` | `RoundtripRenderer` |
| `Meziantou.Framework.Markdown.Parsers` | The block and inline parsers, to write your own |
| `Meziantou.Framework.Markdown.Extensions.*` | One namespace per extension, with its options and its syntax elements (`Tables`, `Yaml`, …) |

## Next steps

- [Getting started](02-GettingStarted.md): install the package and convert your first document.
- [Extensions](04-Extensions/01-Overview.md): the syntaxes you can enable.
- [Security](10-Security.md): what to do before you render Markdown written by your users.
- [Coming from Markdig](11-ComingFromMarkdig.md): the differences with the library this one is derived from.
