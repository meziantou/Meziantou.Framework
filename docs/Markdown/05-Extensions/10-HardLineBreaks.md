---
title: Hard line breaks
description: Render every line break of a paragraph as a <br />, as chat and comment boxes do.
---

In CommonMark, the lines of a paragraph are joined: a line break needs two spaces or a backslash at the end of the line.
People who write comments or messages expect a new line to stay a new line.
[HardLineBreaks](cref:Meziantou.Framework.Markdown.MarkdownExtensions.HardLineBreaks) renders every line break of a
paragraph as `<br />`.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_HardLineBreaks;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.HardLineBreaks };
var tree = MarkdownSyntaxTree.ParseText("Roses are red,\nViolets are blue.\n", options);

Assert.Equal("<p>Roses are red,<br />\nViolets are blue.</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

The syntax tree does not change: only the renderer writes the line breaks differently.
