---
title: Definition lists
description: Write glossaries and lists of terms with their definitions, rendered as <dl>.
---

[DefinitionLists](cref:Meziantou.Framework.Markdown.MarkdownExtensions.DefinitionLists) writes glossaries, lists of
options or FAQs, where each term has a definition: the lines of a paragraph are terms when the next line starts with `:`
or `~`, and the definition follows the marker. They are rendered as `<dl>`, `<dt>` and `<dd>`.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_DefinitionLists;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.DefinitionLists };
var tree = MarkdownSyntaxTree.ParseText("Apple\nPear\n:   A fruit.\n:   A tree.\n", options);

// The lines of the paragraph before the marker are the terms, and a marker after a definition starts another one
var list = Assert.IsType<MarkdownDefinitionListSyntax>(tree.GetRoot().Blocks.Single());
Assert.Equal(["Apple", "Pear"], list.Items[0].Terms.Select(term => term.GetPlainText()).ToArray());
Assert.Empty(list.Items[1].Terms);
Assert.Equal("<dl>\n<dt>Apple</dt>\n<dt>Pear</dt>\n<dd>A fruit.</dd>\n<dd>A tree.</dd>\n</dl>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

## Syntax

- The content of the definition starts four columns from the start of the line, such as `: definition`.
- The next lines of a definition are indented by four columns, so that a definition can have several blocks.
- Another marker starts the next definition of the same terms.

## Syntax tree

A [MarkdownDefinitionListSyntax](cref:Meziantou.Framework.Markdown.MarkdownDefinitionListSyntax) has
[MarkdownDefinitionItemSyntax](cref:Meziantou.Framework.Markdown.MarkdownDefinitionItemSyntax) definitions, each with
its terms and its blocks.
