---
title: Plain text and other formats
description: Convert Markdown to plain text for search indexes and previews, normalize Markdown, or write a renderer for another format.
---

## Plain text

`MarkdownConverter.ToPlainText` renders the text of the document without any markup, for instance for a search index,
the summary of a page or a notification:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestPlainText.cs;region=MarkdownConverter_ToPlainText;format=md-fenced-code-block;dedent=true -->
```csharp
var text = MarkdownConverter.ToPlainText("# Title\n\nSome **bold** text and [a link](https://example.com).\n\n- First\n- Second");
Assert.Equal("Title\nSome bold text and a link.\nFirst\nSecond\n", text);
```
<!-- endref -->

- Each block ends with a line break; the blank lines between blocks are not written.
- Links and images are replaced by their text; the URLs are dropped.
- The text is not HTML-escaped: `&amp;` becomes `&`, and raw HTML is written without its tags.
- Pass a pipeline to read the syntax of the extensions. An overload writes to a `TextWriter`.

## Normalized Markdown

`MarkdownConverter.Normalize` parses the Markdown and writes it back in a consistent style: `#` headings instead of
underlined ones, one blank line between blocks. `NormalizeOptions` (namespace
`Meziantou.Framework.Markdown.Renderers.Normalize`) changes the style:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestNormalize.cs;region=NormalizeOptions_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = "Title\n=====\n* First\n* Second";

Assert.Equal("# Title\n\n* First\n* Second", MarkdownConverter.Normalize(markdown).ReplaceLineEndings("\n"));

var options = new NormalizeOptions
{
    ListItemCharacter = '-',      // null keeps the bullet of the source
    EmptyLineAfterHeading = false,
};
Assert.Equal("# Title\n- First\n- Second", MarkdownConverter.Normalize(markdown, options).ReplaceLineEndings("\n"));
```
<!-- endref -->

| Option | Default | Effect |
| --- | --- | --- |
| `ListItemCharacter` | `null` | The bullet of the list items (`-`, `*` or `+`); `null` keeps the bullet of the source |
| `EmptyLineAfterHeading` | `true` | Adds a blank line after a heading |
| `EmptyLineAfterCodeBlock` | `true` | Adds a blank line after a code block |
| `EmptyLineAfterThematicBreak` | `true` | Adds a blank line after a thematic break |
| `SpaceAfterQuoteBlock` | `true` | Adds a space after the `>` of a quote |
| `ExpandAutoLinks` | `true` | Expands automatic links to the normal inline representation |

To normalize a document after changing its tree, see
[Normalize the document](07-EditingDocuments.md#normalize-the-document). To write it back without changing its
formatting, see [Keep the formatting](07-EditingDocuments.md#keep-the-formatting-roundtrip).

## Other formats

A renderer converts the tree to an output format. To write another format, such as a terminal output or another markup
language:

1. Derive a class from `TextRendererBase<T>` (namespace `Meziantou.Framework.Markdown.Renderers`) for a text format, or
   implement `IMarkdownRenderer`.
2. Add one object renderer per element type to `ObjectRenderers`: a class derived from
   `MarkdownObjectRenderer<TRenderer, TObject>` whose `Write` method writes the element, and calls
   `renderer.WriteChildren` or `renderer.WriteLeafInline` for its content.
3. Render a document with `renderer.Render(document)` after `pipeline.Setup(renderer)`, or call
   `MarkdownConverter.Convert(markdown, renderer, pipeline)`.

The HTML, normalize and roundtrip renderers are built this way: their object renderers, in the
`Meziantou.Framework.Markdown.Renderers.Html`, `.Normalize` and `.Roundtrip` namespaces, are examples to start from. To
change how the HTML renderer writes one element, replace its object renderer instead; see
[Writing extensions](09-WritingExtensions.md#change-how-an-element-is-rendered).
