---
title: Plain text and other formats
description: Convert Markdown to plain text for search indexes and previews, normalize Markdown, escape text to write Markdown, or write a renderer for another format.
---

## Plain text

[MarkdownConverter.ToPlainText](cref:Meziantou.Framework.Markdown.MarkdownConverter.ToPlainText) renders the text of the document without any markup, for instance for a search index,
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

[MarkdownConverter.Normalize](cref:Meziantou.Framework.Markdown.MarkdownConverter.Normalize) parses the Markdown and writes it back in a consistent style: `#` headings instead of
underlined ones, one blank line between blocks. [NormalizeOptions](cref:Meziantou.Framework.Markdown.Renderers.Normalize.NormalizeOptions) (namespace
[Meziantou.Framework.Markdown.Renderers.Normalize](cref:Meziantou.Framework.Markdown.Renderers.Normalize)) changes the style:

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
| [ListItemCharacter](cref:Meziantou.Framework.Markdown.Renderers.Normalize.NormalizeOptions.ListItemCharacter) | `null` | The bullet of the list items (`-`, `*` or `+`); `null` keeps the bullet of the source |
| [EmptyLineAfterHeading](cref:Meziantou.Framework.Markdown.Renderers.Normalize.NormalizeOptions.EmptyLineAfterHeading) | `true` | Adds a blank line after a heading |
| [EmptyLineAfterCodeBlock](cref:Meziantou.Framework.Markdown.Renderers.Normalize.NormalizeOptions.EmptyLineAfterCodeBlock) | `true` | Adds a blank line after a code block |
| [EmptyLineAfterThematicBreak](cref:Meziantou.Framework.Markdown.Renderers.Normalize.NormalizeOptions.EmptyLineAfterThematicBreak) | `true` | Adds a blank line after a thematic break |
| [SpaceAfterQuoteBlock](cref:Meziantou.Framework.Markdown.Renderers.Normalize.NormalizeOptions.SpaceAfterQuoteBlock) | `true` | Adds a space after the `>` of a quote |
| [ExpandAutoLinks](cref:Meziantou.Framework.Markdown.Renderers.Normalize.NormalizeOptions.ExpandAutoLinks) | `true` | Expands automatic links to the normal inline representation |

To normalize a document after changing its tree, see
[Normalize the document](07-EditingDocuments.md#normalize-the-document). To write it back without changing its
formatting, see [Keep the formatting](07-EditingDocuments.md#keep-the-formatting-roundtrip).

## Write Markdown text

To generate Markdown from text that is not Markdown, such as user input, file names or command lines, escape each part
with [MarkdownEscaper](cref:Meziantou.Framework.Markdown.MarkdownEscaper), so that the parser reads it back as the same text instead of interpreting it:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownEscaper_Usage;format=md-fenced-code-block;dedent=true -->
````csharp
// Escape the text so that Markdown displays it as is: every ASCII punctuation character is escaped
Assert.Equal(@"\*Note\* \[docs\]\!", MarkdownEscaper.Escape("*Note* [docs]!"));

// The delimiters of code are longer than the runs of backticks of the content
Assert.Equal("``a`b``", MarkdownEscaper.CreateCodeSpan("a`b"));
Assert.Equal("`` `code` ``", MarkdownEscaper.CreateCodeSpan("`code`"));
Assert.Equal("```csharp\nvar a = 1;\n```\n", MarkdownEscaper.CreateFencedCodeBlock("var a = 1;", "csharp"));

// Build a link from untrusted parts
var text = MarkdownEscaper.Escape("Docs [v2]");
var destination = MarkdownEscaper.EscapeLinkDestination("https://example.com/a b");
var title = MarkdownEscaper.EscapeLinkTitle("Say \"hi\"");
Assert.Equal("<https://example.com/a b>", destination);
Assert.Equal("\"Say \\\"hi\\\"\"", title);

var html = MarkdownConverter.ToHtml($"[{text}]({destination} {title})");
Assert.Equal("<p><a href=\"https://example.com/a%20b\" title=\"Say &quot;hi&quot;\">Docs [v2]</a></p>\n", html);

// The label of a reference link
Assert.Equal(@"see \[1\]", MarkdownEscaper.EscapeLinkLabel("see [1]"));

// A cell of a pipe table cannot contain a pipe or a line ending
Assert.Equal(@"a \| b c", MarkdownEscaper.EscapeTableCell("a | b\nc"));
````
<!-- endref -->

| Method | Use it for |
|---|---|
| [Escape](cref:Meziantou.Framework.Markdown.MarkdownEscaper.Escape) | Text in a paragraph, a heading, a list item, or the text of a link. Every ASCII punctuation character gets a backslash |
| [CreateCodeSpan](cref:Meziantou.Framework.Markdown.MarkdownEscaper.CreateCodeSpan) | Inline code. The delimiters are longer than the runs of backticks of the content |
| [CreateFencedCodeBlock](cref:Meziantou.Framework.Markdown.MarkdownEscaper.CreateFencedCodeBlock) | A code block with an optional info string, such as the language. The fence is longer than the lines that would close it |
| [EscapeLinkDestination](cref:Meziantou.Framework.Markdown.MarkdownEscaper.EscapeLinkDestination) | The URL of a link or an image. A URL with spaces, or an empty one, is enclosed in angle brackets |
| [EscapeLinkTitle](cref:Meziantou.Framework.Markdown.MarkdownEscaper.EscapeLinkTitle) | The title of a link or an image. The result includes the double quotes |
| [EscapeLinkLabel](cref:Meziantou.Framework.Markdown.MarkdownEscaper.EscapeLinkLabel) | The label of a reference link and of its definition, without the brackets |
| [EscapeTableCell](cref:Meziantou.Framework.Markdown.MarkdownEscaper.EscapeTableCell) | The text of a cell of a pipe table. The line endings become spaces |

Pitfalls:

- Whitespace is not escaped: the spaces at the start and at the end of a line are dropped, four leading spaces start an
  indented code block, and a blank line starts a new paragraph.
- The parser converts the line endings of a code span to spaces, and a code span cannot be empty.
- A code span in a table cell must also escape its pipes: replace `|` with `\|` in the result of `CreateCodeSpan`.
- A link label is at most 999 characters, and the parser matches labels case-insensitively.

## Other formats

A renderer converts the tree to an output format. To write another format, such as a terminal output or another markup
language:

1. Derive a class from [TextRendererBase\<T\>](cref:Meziantou.Framework.Markdown.Renderers.TextRendererBase{T}) (namespace [Meziantou.Framework.Markdown.Renderers](cref:Meziantou.Framework.Markdown.Renderers)) for a text format, or
   implement [IMarkdownRenderer](cref:Meziantou.Framework.Markdown.Renderers.IMarkdownRenderer).
2. Add one object renderer per element type to [ObjectRenderers](cref:Meziantou.Framework.Markdown.Renderers.IMarkdownRenderer.ObjectRenderers): a class derived from
   [MarkdownObjectRenderer\<TRenderer, TObject\>](cref:Meziantou.Framework.Markdown.Renderers.MarkdownObjectRenderer{TRenderer,TObject}) whose [Write](cref:Meziantou.Framework.Markdown.Renderers.MarkdownObjectRenderer{TRenderer,TObject}.Write) method writes the element, and calls
   `renderer.WriteChildren` or `renderer.WriteLeafInline` for its content.
3. Render a document with `renderer.Render(document)` after `pipeline.Setup(renderer)`, or call
   `MarkdownConverter.Convert(markdown, renderer, pipeline)`.

The HTML, normalize and roundtrip renderers are built this way: their object renderers, in the
[Meziantou.Framework.Markdown.Renderers.Html](cref:Meziantou.Framework.Markdown.Renderers.Html), `.Normalize` and `.Roundtrip` namespaces, are examples to start from. To
change how the HTML renderer writes one element, replace its object renderer instead; see
[Writing extensions](09-WritingExtensions.md#change-how-an-element-is-rendered).
