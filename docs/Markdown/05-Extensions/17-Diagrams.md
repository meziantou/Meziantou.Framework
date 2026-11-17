---
title: Diagrams
description: Render code blocks of Mermaid and nomnoml so that their scripts draw the diagrams.
---

Diagram languages, such as [Mermaid](https://mermaid.js.org/), describe flowcharts, sequences or classes as text, which
fits in a Markdown document. [Diagrams](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Diagrams) renders their
code blocks as the elements that the scripts of these languages look for: a code block of `mermaid` is rendered as `<pre
class="mermaid">`, and one of `nomnoml` as `<div class="nomnoml">`. Add the script of the language to the page to draw
the diagrams.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_Diagrams;format=md-fenced-code-block;dedent=true -->
````csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Diagrams };
var tree = MarkdownSyntaxTree.ParseText("```mermaid\ngraph LR\n  A --> B\n```\n\n```nomnoml\n[A]->[B]\n```\n", options);

Assert.Equal("<pre class=\"mermaid\">graph LR\n  A --> B\n</pre>\n<div class=\"nomnoml\">[A]->[B]\n</div>\n", MarkdownHtmlRenderer.ToHtml(tree));
````
<!-- endref -->

The syntax tree does not change: the diagrams are
[MarkdownCodeBlockSyntax](cref:Meziantou.Framework.Markdown.MarkdownCodeBlockSyntax) nodes, whose
[Language](cref:Meziantou.Framework.Markdown.MarkdownCodeBlockSyntax.Language) is `mermaid` or `nomnoml`.
