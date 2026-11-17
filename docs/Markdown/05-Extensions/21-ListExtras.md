---
title: Lettered and roman lists
description: Number ordered lists with letters or roman numbers, such as a. or iv.
---

CommonMark only numbers ordered lists with digits.
[ListExtras](cref:Meziantou.Framework.Markdown.MarkdownExtensions.ListExtras) adds the numberings of legal and academic
documents: letters (`a.`, `B)`) and roman numbers (`iv.`, `X)`), rendered with the `type` attribute of `<ol>`, so that
the browser numbers the items the same way.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_ListExtras;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.ListExtras };
var tree = MarkdownSyntaxTree.ParseText("iv. Four\nv. Five\n", options);

var list = Assert.IsType<MarkdownListSyntax>(tree.GetRoot().Blocks.Single());
Assert.Equal(MarkdownOrderedListStyle.LowerRoman, list.OrderedStyle);
Assert.Equal(4, list.StartNumber);
Assert.Equal("<ol type=\"i\" start=\"4\">\n<li>Four</li>\n<li>Five</li>\n</ol>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

[MarkdownListSyntax.OrderedStyle](cref:Meziantou.Framework.Markdown.MarkdownListSyntax.OrderedStyle) gives the
numbering, and [StartNumber](cref:Meziantou.Framework.Markdown.MarkdownListSyntax.StartNumber) the number of the first
item.

`i`, `v` and `x` are roman numbers unless they continue a list of letters, and a list of letters can interrupt a
paragraph whatever its first letter.
