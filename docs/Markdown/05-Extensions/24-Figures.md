---
title: Figures
description: Group an image, a diagram or code with its caption, rendered as <figure> and <figcaption>.
---

[Figures](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Figures) gives an image, a diagram, a table or a code
sample a caption: blocks between a fence of three carets or more, `^^^`, and a fence as long are rendered as `<figure>`,
and the text after a fence as `<figcaption>`.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_Figures;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Figures };
var tree = MarkdownSyntaxTree.ParseText("^^^\n![Logo](logo.png)\n^^^ The *logo*\n", options);

var figure = Assert.IsType<MarkdownFigureSyntax>(tree.GetRoot().Blocks.Single());
Assert.Equal("The logo", figure.Blocks.OfType<MarkdownFigureCaptionSyntax>().Single().GetPlainText());
Assert.Equal("<figure>\n<p><img src=\"logo.png\" alt=\"Logo\" /></p>\n<figcaption>The <em>logo</em></figcaption>\n</figure>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

A figure is a [MarkdownFigureSyntax](cref:Meziantou.Framework.Markdown.MarkdownFigureSyntax), and its caption a
[MarkdownFigureCaptionSyntax](cref:Meziantou.Framework.Markdown.MarkdownFigureCaptionSyntax). The text after the opening
fence is a caption before the content, and the text after the closing fence a caption after it.
