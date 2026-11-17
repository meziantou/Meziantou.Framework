---
title: Bootstrap
description: Add the classes of Bootstrap to the tables, images, block quotes, figures and alerts of the HTML.
---

[Bootstrap](https://getbootstrap.com/) styles the elements that have its classes.
[Bootstrap](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Bootstrap) makes the renderer add them, so that the
HTML of a document looks like the rest of a Bootstrap site:

| Element | Classes |
| ------- | ------- |
| Tables | `table` |
| Images | `img-fluid` |
| Block quotes | `blockquote` |
| [Figures](24-Figures.md) | `figure`, and `figure-caption` on their captions |
| [Alerts](16-Alerts.md) | `alert` and the class of their kind, with `role="alert"` and `mb-0` on their last paragraph |

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownHtmlRenderer_Bootstrap;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Bootstrap | MarkdownExtensions.PipeTables };
var tree = MarkdownSyntaxTree.ParseText("> Quote\n\n![Logo](logo.png)\n\n| Name |\n|------|\n| Apple |\n", options);

Assert.Equal("<blockquote class=\"blockquote\">\n<p>Quote</p>\n</blockquote>\n<p><img src=\"logo.png\" class=\"img-fluid\" alt=\"Logo\" /></p>\n<table class=\"table\">\n<thead>\n<tr>\n<th>Name</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>Apple</td>\n</tr>\n</tbody>\n</table>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

The classes come after the ones of the [generic attributes](15-GenericAttributes.md). The syntax tree does not change.
