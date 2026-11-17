---
title: Footers
description: Attribute a quote or add a footer with lines that start with ^^, rendered as <footer>.
---

[Footers](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Footers) adds the footer of a section, such as the author
of a quote: lines that start with `^^`, as a block quote does with `>`, are rendered as `<footer>`, with its paragraphs
without `<p>`. A blank line ends it.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_Footers;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Footers };
var tree = MarkdownSyntaxTree.ParseText("> Simplicity is prerequisite for reliability.\n^^ Edsger W. Dijkstra\n", options);

Assert.IsType<MarkdownFooterSyntax>(tree.GetRoot().Blocks[1]);
Assert.Equal("<blockquote>\n<p>Simplicity is prerequisite for reliability.</p>\n</blockquote>\n<footer>Edsger W. Dijkstra</footer>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

A footer is a [MarkdownFooterSyntax](cref:Meziantou.Framework.Markdown.MarkdownFooterSyntax), whose
[Blocks](cref:Meziantou.Framework.Markdown.MarkdownFooterSyntax.Blocks) are its content.
