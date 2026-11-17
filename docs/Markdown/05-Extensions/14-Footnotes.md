---
title: Footnotes
description: Refer to notes with [^1], and render them at the end of the document with links back and forth.
---

[Footnotes](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Footnotes) adds notes that do not interrupt the text,
such as sources or remarks: `[^1]` refers to a note, and `[^1]: text` defines it, anywhere in the document. The renderer
writes the notes at the end of the document, numbered in the order of their first reference, with links from the
references to the notes and back.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_Footnotes;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Footnotes };
var tree = MarkdownSyntaxTree.ParseText("Markdown was created in 2004[^1].\n\n[^1]: By John Gruber.\n", options);

var reference = tree.GetRoot().DescendantNodes().OfType<MarkdownFootnoteReferenceSyntax>().Single();
Assert.Equal("By John Gruber.", reference.Definition!.GetPlainText());
Assert.Equal("<p>Markdown was created in 2004<a id=\"fnref:1\" href=\"#fn:1\" class=\"footnote-ref\"><sup>1</sup></a>.</p>\n<div class=\"footnotes\">\n<hr />\n<ol>\n<li id=\"fn:1\">\n<p>By John Gruber.<a href=\"#fnref:1\" class=\"footnote-back-ref\">&#8617;</a></p>\n</li>\n</ol>\n</div>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

## Syntax

- The label of a note is any text, such as `[^1]` or `[^source]`: the notes are numbered in the order of their first
  reference, whatever their labels.
- The next lines of a definition are indented by four spaces, so that a note can have several paragraphs, lists or code
  blocks.
- A note that no text refers to is not written.

## Syntax tree

A definition is a
[MarkdownFootnoteDefinitionSyntax](cref:Meziantou.Framework.Markdown.MarkdownFootnoteDefinitionSyntax), and a reference
a [MarkdownFootnoteReferenceSyntax](cref:Meziantou.Framework.Markdown.MarkdownFootnoteReferenceSyntax), whose
[Definition](cref:Meziantou.Framework.Markdown.MarkdownFootnoteReferenceSyntax.Definition) gives the note.
[MarkdownDocumentSyntax.GetFootnoteDefinition](cref:Meziantou.Framework.Markdown.MarkdownDocumentSyntax.GetFootnoteDefinition)
finds a note by its label.
