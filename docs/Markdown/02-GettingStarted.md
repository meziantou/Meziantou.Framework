---
title: Getting started
description: Install the package, convert Markdown to HTML, enable extensions and read the syntax tree.
---

## Install the package

Add the `Meziantou.Framework.Markdown` package to a project:

```bash
dotnet add package Meziantou.Framework.Markdown
```

The examples of this documentation omit the `using` directives. The first ones only need
`using Meziantou.Framework.Markdown;`; see [Namespaces](01-Introduction.md#namespaces) for the others.

## Convert Markdown to HTML

[MarkdownConverter.ToHtml](cref:Meziantou.Framework.Markdown.MarkdownConverter.ToHtml) converts a Markdown string to HTML:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownConverter_ToHtml;format=md-fenced-code-block;dedent=true -->
```csharp
var html = MarkdownConverter.ToHtml("This is a text with some *emphasis*");
Assert.Equal("<p>This is a text with some <em>emphasis</em></p>\n", html);
```
<!-- endref -->

Without a pipeline, the conversion follows the CommonMark specification, with no extension. Raw HTML in the Markdown is
copied to the output: read [Security](10-Security.md) before you render Markdown from an untrusted source.

## Enable extensions

Tables, task lists, footnotes and the other syntaxes that CommonMark does not define come from extensions. Build a
pipeline that enables them, and pass it to [ToHtml](cref:Meziantou.Framework.Markdown.MarkdownConverter.ToHtml):

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownConverter_Pipeline;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()
    .Build();

var html = MarkdownConverter.ToHtml("| a | b |\n|---|---|\n| 1 | 2 |", pipeline);
Assert.StartsWith("<table>", html);
```
<!-- endref -->

[UseAdvancedExtensions](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseAdvancedExtensions) enables the most common extensions at once. Build the pipeline once and reuse it: it is
immutable and can be shared between threads. [Extensions](04-Extensions/01-Overview.md) lists all the extensions.

## Read the syntax tree

[MarkdownConverter.Parse](cref:Meziantou.Framework.Markdown.MarkdownConverter.Parse) returns the syntax tree of the document. Use it to find elements, such as the headings and the
links:

<!-- ref:../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownConverter_SyntaxTree;format=md-fenced-code-block;dedent=true -->
```csharp
var document = MarkdownConverter.Parse("# Title\n\nSee [the docs](https://example.com).");

foreach (var heading in document.Descendants<HeadingBlock>())
{
    Assert.Equal(1, heading.Level);
    Assert.Equal(0, heading.Line);
}

foreach (var link in document.Descendants<LinkInline>())
{
    Assert.Equal("https://example.com", link.Url);
}
```
<!-- endref -->

## Next steps

- [Converting to HTML](03-ConvertingToHtml.md): reuse a pipeline, write to a stream, resolve relative links.
- [Extensions](04-Extensions/01-Overview.md): the syntaxes you can enable, and their options.
- [The syntax tree](05-SyntaxTree.md) and [Editing documents](07-EditingDocuments.md): read and change documents.
