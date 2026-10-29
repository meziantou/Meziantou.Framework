---
title: Converting to HTML
description: Convert Markdown to HTML with a pipeline, write to a TextWriter, resolve relative links and choose the line endings.
---

## Convert a string

`MarkdownConverter.ToHtml` parses the Markdown and renders it to an HTML string. Pass a pipeline to enable extensions:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownConverter_PipelineExtensions;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UsePipeTables()
    .UseTaskLists()
    .UseAutoIdentifiers()
    .Build();

var html = MarkdownConverter.ToHtml("# Getting started", pipeline);
Assert.Equal("<h1 id=\"getting-started\">Getting started</h1>\n", html);
```
<!-- endref -->

The output is an HTML fragment, without `<html>` or `<body>`: insert it into your page or template.

## Build the pipeline once

A `MarkdownPipelineBuilder` collects the extensions, and `Build` creates the `MarkdownPipeline`:

- **Reuse the pipeline.** Building it sets up every parser and renderer. A pipeline is immutable and can be shared
  between threads: store it in a static field or register it as a singleton.
- **Do not share the builder.** A `MarkdownPipelineBuilder` is not thread-safe; once built, it returns the same pipeline.
- **Use the same pipeline everywhere.** Parse and render a document with the same pipeline: an extension adds the parser
  of its syntax and the renderer of its elements.

[Extensions](04-Extensions/01-Overview.md) lists the extensions and the ways to enable them, including from a
configuration string.

## Write to a TextWriter

To write the HTML to a stream or a response instead of allocating a string, pass a `TextWriter`. This overload also
returns the parsed document:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownConverter_ToHtmlWriter;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

using var writer = new StringWriter();
var document = MarkdownConverter.ToHtml("# Title\n\nSome text.", writer, pipeline);

Assert.Equal("<h1 id=\"title\">Title</h1>\n<p>Some text.</p>\n", writer.ToString());
Assert.HasCount(2, document); // The parsed document is returned too: a heading and a paragraph
```
<!-- endref -->

To parse a document once and render it later, for instance after reading or changing its tree, call
`MarkdownConverter.Parse`, then `document.ToHtml(pipeline)` or `document.ToHtml(writer, pipeline)`. See
[Editing documents](07-EditingDocuments.md).

## Code blocks

A fenced code block is rendered as `<pre><code>`. The language written after the opening fence gives the class
`language-<name>` that syntax highlighters such as Prism or highlight.js look for: a `csharp` block renders
`<code class="language-csharp">`. The code itself is HTML-escaped and not highlighted.

## Resolve relative links

To render links and images for another location, create an `HtmlRenderer` yourself and set its properties:

- `BaseUrl` resolves the relative URLs of the links and the images against a base URL. Absolute URLs are not changed.
- `LinkRewriter` is called for the URL of every link and image, after `BaseUrl` is applied, and returns the URL to
  write.

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestLinkRewriter.cs;region=HtmlRenderer_BaseUrlAndLinkRewriter;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder().Build();
var document = MarkdownConverter.Parse("[Next page](next.md) ![Logo](logo.png)", pipeline);

using var writer = new StringWriter();
var renderer = new HtmlRenderer(writer)
{
    // Resolves the relative URLs of links and images
    BaseUrl = new Uri("https://example.com/docs/"),

    // Called for every URL of a link or an image, after BaseUrl is applied
    LinkRewriter = url => url.EndsWith(".md", StringComparison.Ordinal) ? url[..^3] + ".html" : url,
};
pipeline.Setup(renderer); // Adds the renderers of the extensions of the pipeline
renderer.Render(document);

Assert.Equal("<p><a href=\"https://example.com/docs/next.html\">Next page</a> <img src=\"https://example.com/docs/logo.png\" alt=\"Logo\" /></p>\n", writer.ToString());
```
<!-- endref -->

Call `pipeline.Setup(renderer)` before rendering: it adds the renderers of the extensions of the pipeline. Without it,
the elements of the extensions lose their markup: a table is written as the paragraphs of its cells.

The other properties of `HtmlRenderer` change the output for special cases:

| Property | Effect |
| --- | --- |
| `ImplicitParagraph` | Omits the `<p>` tags around paragraphs, to render a single line of text inline |
| `EnableHtmlForBlock`, `EnableHtmlForInline` | When `false`, writes the text without the HTML tags of the blocks or of the inlines |
| `EnableHtmlEscape` | When `false`, writes the text without escaping `<`, `>` and `&` |
| `UseNonAsciiNoEscape` | Writes the non-ASCII characters of URLs as they are; see [`UseNonAsciiNoEscape`](04-Extensions/03-HeadingsAndLinks.md#non-ascii-urls) |

## Line breaks and line endings

A single line break in a paragraph is a soft break: CommonMark renders it as a line break in the HTML source, which
browsers display as a space. To display it as a line break, as in comments and chat messages, use
`UseSoftlineBreakAsHardlineBreak`:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_UseSoftlineBreakAsHardlineBreak;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseSoftlineBreakAsHardlineBreak()
    .Build();

var html = MarkdownConverter.ToHtml("First line\nSecond line", pipeline);
Assert.Equal("<p>First line<br />\nSecond line</p>\n", html);
```
<!-- endref -->

The renderers write `\n` at the end of each line, whatever the line endings of the source. `ConfigureNewLine` sets
another line ending:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestConfigureNewLine.cs;region=MarkdownExtensions_ConfigureNewLine;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .ConfigureNewLine("\r\n")
    .Build();

var html = MarkdownConverter.ToHtml("# Title\n\nSome text.", pipeline);
Assert.Equal("<h1>Title</h1>\r\n<p>Some text.</p>\r\n", html);
```
<!-- endref -->

## Raw HTML and untrusted input

CommonMark allows raw HTML, and it is copied to the output unchanged. Links are not filtered either. Before you render
Markdown written by your users, read [Security](10-Security.md).
