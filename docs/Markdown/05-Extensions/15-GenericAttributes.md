---
title: Generic attributes
description: Set the id, the classes and other HTML attributes of a heading, a block, a link or another inline with {#id .class name=value}.
---

Markdown has no way to give an element an `id` to link to, or a class to style it.
[GenericAttributes](cref:Meziantou.Framework.Markdown.MarkdownExtensions.GenericAttributes) adds
`{#id .class name=value}`, which sets the HTML attributes of the element before it: `#` gives the `id`, `.` adds a
class, and `name=value` or `name="value"` sets an attribute.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_GenericAttributes;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.GenericAttributes };
var tree = MarkdownSyntaxTree.ParseText("# Introduction {#intro .lead}\n\nSee [the docs](https://example.com){title=\"Documentation\" onclick=\"run()\"}.\n\n{.note}\nA note.\n", options);

var attributes = tree.GetRoot().DescendantNodes().OfType<MarkdownGenericAttributesSyntax>().First();
Assert.Equal("intro", attributes.Id);
Assert.Equal(["lead"], attributes.Classes);
Assert.IsType<MarkdownHeadingSyntax>(attributes.Target);

// The renderer does not write the event handlers
Assert.Equal("<h1 id=\"intro\" class=\"lead\">Introduction</h1>\n<p>See <a href=\"https://example.com\" title=\"Documentation\">the docs</a>.</p>\n<p class=\"note\">A note.</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

## Which element gets the attributes

- After an inline that is not text, such as a link, a code span or an emphasis, the attributes are for it.
- After text, they are for the block.
- At the end of the line of a heading, or after the info string of a fenced block, they are for the block.
- A paragraph of attributes only gives them to the next block.

The attributes are [MarkdownGenericAttributesSyntax](cref:Meziantou.Framework.Markdown.MarkdownGenericAttributesSyntax)
nodes: [Id](cref:Meziantou.Framework.Markdown.MarkdownGenericAttributesSyntax.Id),
[Classes](cref:Meziantou.Framework.Markdown.MarkdownGenericAttributesSyntax.Classes) and
[Properties](cref:Meziantou.Framework.Markdown.MarkdownGenericAttributesSyntax.Properties) give their values, and
[Target](cref:Meziantou.Framework.Markdown.MarkdownGenericAttributesSyntax.Target) the node they are for.

## Attributes that run script

By default, the renderer only writes the attributes that describe the content, such as `title`, `lang` or `aria-*`:
event handlers, URL attributes, `style` or `data-*` can run script when the Markdown is not trusted.
[MarkdownHtmlRendererOptions.AttributeFilter](cref:Meziantou.Framework.Markdown.MarkdownHtmlRendererOptions.AttributeFilter)
changes this. See [Security](../07-Security.md).
