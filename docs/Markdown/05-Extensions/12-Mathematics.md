---
title: Mathematics
description: Write formulas in LaTeX between $ or $$, and render them for MathJax or KaTeX.
---

[Mathematics](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Mathematics) lets technical and scientific documents
contain formulas written in LaTeX: inline math between `$` or `$$`, and math blocks between `$$` lines. Markdown does
not change the formulas: emphasis, escapes and HTML are not read in them.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_Mathematics;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Mathematics };
var tree = MarkdownSyntaxTree.ParseText("The energy is $E = mc^2$.\n\n$$\n\\sum_{i=1}^n i\n$$\n", options);

Assert.Equal("E = mc^2", tree.GetRoot().DescendantNodes().OfType<MarkdownMathInlineSyntax>().Single().Math);
Assert.Equal("\\sum_{i=1}^n i\n", Assert.IsType<MarkdownMathBlockSyntax>(tree.GetRoot().Blocks[1]).Math);
Assert.Equal("<p>The energy is <span class=\"math\">\\(E = mc^2\\)</span>.</p>\n<div class=\"math\">\n\\[\n\\sum_{i=1}^n i\n\\]</div>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

The renderer writes inline math as `<span class="math">\(...\)</span>` and blocks as `<div class="math">\[...\]</div>`,
the delimiters that MathJax and KaTeX look for: add one of them to the page to draw the formulas.

Inline math is a [MarkdownMathInlineSyntax](cref:Meziantou.Framework.Markdown.MarkdownMathInlineSyntax) and a block a
[MarkdownMathBlockSyntax](cref:Meziantou.Framework.Markdown.MarkdownMathBlockSyntax): their `Math` property gives the
LaTeX.
