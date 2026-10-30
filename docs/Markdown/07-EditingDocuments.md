---
title: Editing documents
description: Change the syntax tree and render it again, write a document back as normalized Markdown, or keep its formatting with a roundtrip.
---

## Change the tree and render it

Parse the document, change its elements, then render it with the same pipeline. For instance, to switch the links to
HTTPS:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownConverter_ChangeTree;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = "See [the docs](http://example.com).";
var pipeline = new MarkdownPipelineBuilder().Build();
var document = MarkdownConverter.Parse(markdown, pipeline);
foreach (var link in document.Descendants<LinkInline>())
{
    link.Url = link.Url?.Replace("http://", "https://", StringComparison.Ordinal);
}

var html = document.ToHtml(pipeline);
Assert.Equal("<p>See <a href=\"https://example.com\">the docs</a>.</p>\n", html);
```
<!-- endref -->

To change every document that a pipeline parses, change the tree in the
[`DocumentProcessed`](09-WritingExtensions.md#process-the-document-after-parsing) event instead.

## Add and remove elements

Blocks and inlines are created with their constructors and added to their parent: a [ContainerBlock](cref:Meziantou.Framework.Markdown.Syntax.ContainerBlock) is a list of
blocks, and [ContainerInline.AppendChild](cref:Meziantou.Framework.Markdown.Syntax.Inlines.ContainerInline.AppendChild) adds an inline. [Remove()](cref:Meziantou.Framework.Markdown.Syntax.Block.Remove) removes an element from its parent, and
[ReplaceBy](cref:Meziantou.Framework.Markdown.Syntax.Block.ReplaceBy) replaces it:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownDocument_AddAndRemoveBlocks;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder().Build();
var document = MarkdownConverter.Parse("# Release notes\n\n<!-- TODO -->\n\nFirst version.", pipeline);

// Remove the HTML comments
foreach (var block in document.Descendants<HtmlBlock>().ToList())
{
    block.Remove();
}

// Add a paragraph at the end: "Thanks to **all** the contributors."
var emphasis = new EmphasisInline { DelimiterChar = '*', DelimiterCount = 2 };
emphasis.AppendChild(new LiteralInline("all"));
var paragraph = new ParagraphBlock { Inline = new ContainerInline() };
paragraph.Inline
    .AppendChild(new LiteralInline("Thanks to "))
    .AppendChild(emphasis)
    .AppendChild(new LiteralInline(" the contributors."));
document.Add(paragraph);

var html = document.ToHtml(pipeline);
Assert.Equal("<h1>Release notes</h1>\n<p>First version.</p>\n<p>Thanks to <strong>all</strong> the contributors.</p>\n", html);
```
<!-- endref -->

- **Copy the list before removing elements in a loop.** [Descendants](cref:Meziantou.Framework.Markdown.Syntax.MarkdownObjectExtensions.Descendants) walks the tree while you change it: call
  `ToList()` first, as above.
- **Inlines live in leaf blocks.** A new [ParagraphBlock](cref:Meziantou.Framework.Markdown.Syntax.ParagraphBlock) or [HeadingBlock](cref:Meziantou.Framework.Markdown.Syntax.HeadingBlock) needs an [Inline](cref:Meziantou.Framework.Markdown.Syntax.Inlines.Inline) container for its text.
- **Positions are not updated.** The new elements have no position, and the others keep the position they had in the
  source.

## Write the document back as Markdown

Two renderers write Markdown:

| Renderer | Output | Use it to |
| --- | --- | --- |
| [NormalizeRenderer](cref:Meziantou.Framework.Markdown.Renderers.Normalize.NormalizeRenderer) | Markdown in a consistent style: `#` headings, one blank line between blocks | Clean up or generate Markdown |
| [RoundtripRenderer](cref:Meziantou.Framework.Markdown.Renderers.Roundtrip.RoundtripRenderer) | The source exactly as it was parsed, with your changes | Change a file that people edit, without reformatting it |

### Normalize the document

[MarkdownConverter.Normalize](cref:Meziantou.Framework.Markdown.MarkdownConverter.Normalize) converts a Markdown string to normalized Markdown. To normalize a document you changed,
create a [NormalizeRenderer](cref:Meziantou.Framework.Markdown.Renderers.Normalize.NormalizeRenderer) and set it up with the pipeline:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestNormalize.cs;region=NormalizeRenderer_Document;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder().Build();
var document = MarkdownConverter.Parse("Title\n=====\n\nSee [the docs](http://example.com).", pipeline);
foreach (var link in document.Descendants<LinkInline>())
{
    link.Url = link.Url?.Replace("http://", "https://", StringComparison.Ordinal);
}

using var writer = new StringWriter();
var renderer = new NormalizeRenderer(writer);
pipeline.Setup(renderer);
renderer.Render(document);

Assert.Equal("# Title\n\nSee [the docs](https://example.com).", writer.ToString().ReplaceLineEndings("\n"));
```
<!-- endref -->

[Normalized Markdown](08-PlainTextAndOtherFormats.md#normalized-markdown) describes the options of the output.

### Keep the formatting (roundtrip)

The parser drops the characters that do not change the meaning of the document: extra spaces, the style of a heading,
the line endings. With trivia tracking, it keeps them in the tree, and [RoundtripRenderer](cref:Meziantou.Framework.Markdown.Renderers.Roundtrip.RoundtripRenderer) (namespace
[Meziantou.Framework.Markdown.Renderers.Roundtrip](cref:Meziantou.Framework.Markdown.Renderers.Roundtrip)) writes the document back exactly as it was:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownConverter_Roundtrip;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = "#   Title  \n\n*  item\n";
var document = MarkdownConverter.Parse(markdown, trackTrivia: true);

// Change the tree here...

using var writer = new StringWriter();
var renderer = new RoundtripRenderer(writer);
renderer.Write(document);
var output = writer.ToString();
Assert.Equal(markdown, output); // Same as markdown when nothing changed
```
<!-- endref -->

`Parse(markdown, trackTrivia: true)` uses a pipeline without extensions. To use extensions, call [EnableTrackTrivia()](cref:Meziantou.Framework.Markdown.MarkdownExtensions.EnableTrackTrivia)
on the builder.

The trivia is stored in the tree: blank lines in [Block.LinesBefore](cref:Meziantou.Framework.Markdown.Syntax.Block.LinesBefore) and [Block.LinesAfter](cref:Meziantou.Framework.Markdown.Syntax.Block.LinesAfter), and the other characters in
the properties named `Trivia…` ([TriviaBefore](cref:Meziantou.Framework.Markdown.Syntax.Block.TriviaBefore), [TriviaAfter](cref:Meziantou.Framework.Markdown.Syntax.Block.TriviaAfter)…) of the blocks and the inlines. These properties are
only filled when trivia tracking is enabled.

Values also keep their source text, in the properties named `Unescaped…`, and the roundtrip renderer writes this text
when it is set. To change such a value, change both properties. The text is written as is, so it must be valid
Markdown:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=RoundtripRenderer_ChangeLink;format=md-fenced-code-block;dedent=true -->
```csharp
var markdown = "Read   [the  docs](http://example.com 'Docs')  first.\n\n*   item\n";
var document = MarkdownConverter.Parse(markdown, trackTrivia: true);

foreach (var link in document.Descendants<LinkInline>())
{
    // The roundtrip renderer writes the URL as it is in the source (UnescapedUrl): change both
    link.Url = "https://example.com";
    link.UnescapedUrl = new StringSlice(link.Url);
}

using var writer = new StringWriter();
new RoundtripRenderer(writer).Write(document);

// Only the URL changed: the spaces, the quotes and the list marker are the same
Assert.Equal("Read   [the  docs](https://example.com 'Docs')  first.\n\n*   item\n", writer.ToString());
```
<!-- endref -->

Use trivia tracking for the roundtrip only: the HTML of a document parsed with trivia keeps some of the extra
whitespace of the source, for instance at the start of a heading.

### Change the attributes

With [`UseGenericAttributes`](04-Extensions/06-GenericAttributes.md), the roundtrip renderer writes the attributes that
you change, as `{#id .class name=value}`:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=RoundtripRenderer_ChangeAttributes;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .EnableTrackTrivia()
    .UseGenericAttributes()
    .Build();
var document = MarkdownConverter.Parse("# Introduction\n\nSome text.\n", pipeline);

document.Descendants<HeadingBlock>().First().GetAttributes().Id = "intro";
document.Descendants<ParagraphBlock>().First().GetAttributes().AddClass("lead");

using var writer = new StringWriter();
var renderer = new RoundtripRenderer(writer);
pipeline.Setup(renderer);
renderer.Write(document);

Assert.Equal("# Introduction {#intro}\n\nSome text. {.lead}\n", writer.ToString());
```
<!-- endref -->

The new attributes replace the attributes of the source attached to the same element; when there are several, the
others are written as empty attributes, `{}`, so that the lines stay the same. An element without attributes in the
source gets them where the parser reads them back:

| Element | Where the attributes are written |
| --- | --- |
| Paragraph, heading | At the end of the text |
| Fenced code block, math block, custom container | At the end of the opening fence line |
| Quote, list, thematic break, HTML block | On a line before the block, for a block of the document that follows a blank line |
| Link, image, autolink, code span, raw HTML | Right after the inline |

The attributes that the parser adds without them being in the source, such as an identifier computed from the text of
a heading or the class of the language of a fenced code block, are not written while they are unchanged.

The renderer throws an `InvalidOperationException` when the parser would not read the attributes back:

- a value cannot be written, such as an identifier with a space or a value with a line break;
- the element has no syntax for attributes: a list item, a table, an indented code block, a literal;
- the text before them would read them: an unfinished `{name=`, an unclosed code span, a link found by the autolinks
  extension;
- the last line of a paragraph would then start another block, such as `*` or `|`;
- a new attribute line would join the paragraph before the block.

Emphasis keeps the attributes of the source, but cannot get new ones: they would change which delimiters are emphasis.
