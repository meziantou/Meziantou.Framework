---
title: Attributes
description: Add identifiers, classes and HTML attributes to blocks and inlines with {#id .class name=value}, and choose the allowed attributes.
---

## Add attributes in Markdown

`UseGenericAttributes` reads attributes written between `{` and `}` after a block or an inline: `#id` sets the
identifier, `.class` adds a class, and `name=value` adds an attribute. Put them at the end of a heading or a paragraph,
at the end of the opening fence of a code block, or right after a link, an image or a code span:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestHtmlAttributes.cs;region=MarkdownExtensions_UseGenericAttributes;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseGenericAttributes()
    .Build();

var html = MarkdownConverter.ToHtml("# Introduction {#intro .title}\n\nRead [the guide](guide.html){target=_blank}.", pipeline);
Assert.Equal("<h1 id=\"intro\" class=\"title\">Introduction</h1>\n<p>Read <a href=\"guide.html\" target=\"_blank\">the guide</a>.</p>\n", html);
```
<!-- endref -->

Write a value that contains spaces between quotes: `{title="A long title"}`. Enable this extension after the others:
it changes the parsers that they add. `UseAdvancedExtensions` enables it last.

## Choose the allowed attributes

By default, only the identifier, the classes and the attributes that describe the content are written, such as
`title`, `lang`, `alt`, `colspan`, `target`, `role` and the `aria-*` attributes. The other attributes are dropped: the
event handlers (`onclick`…), the attributes that hold a URL, `style`, `data-*` and the directives of client-side
frameworks (`x-init`, `hx-get`…) can run a script or cover the page. [Security](../10-Security.md) gives the full list.

When the Markdown is trusted, pass a filter to allow more attributes. `GenericAttributesExtension.IsSafeAttributeName`
(namespace [Meziantou.Framework.Markdown.Extensions.GenericAttributes](cref:Meziantou.Framework.Markdown.Extensions.GenericAttributes)) is the default filter:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownConverter_GenericAttributesFilter;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()
    .UseGenericAttributes(name => GenericAttributesExtension.IsSafeAttributeName(name) || name.StartsWith("data-", StringComparison.OrdinalIgnoreCase))
    .Build();

var html = MarkdownConverter.ToHtml("Text {data-id=1 style=color:red}", pipeline);
Assert.Equal("<p data-id=\"1\">Text</p>\n", html);
```
<!-- endref -->

`_ => true` allows every attribute.

## Set attributes from code

`GetAttributes()` returns the `HtmlAttributes` of any element of the [syntax tree](../05-SyntaxTree.md), and creates
them when the element has none. The HTML renderer writes them, with or without `UseGenericAttributes`, and the filter
does not apply to them:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestHtmlAttributes.cs;region=HtmlAttributes_FromCode;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder().Build();
var document = MarkdownConverter.Parse("> Prefer the async methods.", pipeline);

// Attributes can be set from code, with or without UseGenericAttributes
var quote = document.Descendants<QuoteBlock>().Single();
var attributes = quote.GetAttributes();
attributes.AddClass("note");
attributes.AddProperty("style", "border-left: 4px solid gray"); // Not filtered: the code is trusted

var html = document.ToHtml(pipeline);
Assert.Equal("<blockquote class=\"note\" style=\"border-left: 4px solid gray\">\n<p>Prefer the async methods.</p>\n</blockquote>\n", html);
```
<!-- endref -->

`HtmlAttributes` has `Id`, `Classes` and `Properties`, and the `AddClass`, `AddProperty` and `AddPropertyIfNotExist`
methods. `TryGetAttributes()` returns `null` instead of creating them. To set attributes on every document, change the
tree in [`DocumentProcessed`](../09-WritingExtensions.md#process-the-document-after-parsing).

To write the attributes back to the Markdown source, see
[Editing documents](../07-EditingDocuments.md#change-the-attributes).
