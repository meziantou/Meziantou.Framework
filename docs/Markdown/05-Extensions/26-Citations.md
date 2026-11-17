---
title: Citations
description: Mark the title of a work, such as a book or a film, with "" so that it is rendered as <cite>.
---

[Citations](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Citations) marks the title of a creative work, such as
a book, an article or a film: text between `""` is rendered as `<cite>`, which browsers show in italics.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_Citations;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Citations };
var tree = MarkdownSyntaxTree.ParseText("As \"\"The Pragmatic Programmer\"\" says.\n", options);

var citation = tree.GetRoot().DescendantNodes().OfType<MarkdownEmphasisExtraSyntax>().Single();
Assert.Equal(MarkdownEmphasisExtraStyle.Citation, citation.Style);
Assert.Equal("<p>As <cite>The Pragmatic Programmer</cite> says.</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

A citation is a [MarkdownEmphasisExtraSyntax](cref:Meziantou.Framework.Markdown.MarkdownEmphasisExtraSyntax) of style
`Citation`. As `_`, the quotes cannot open or close in a word.
