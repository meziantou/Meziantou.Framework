---
title: Strikethrough
description: Strike text out with two tildes, as GitHub does.
---

[Strikethrough](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Strikethrough) marks deleted text, such as a
correction that should stay visible: text between `~~` is rendered as `<del>`. It is the strikethrough of GitHub
Flavored Markdown.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_Strikethrough;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Strikethrough };
var tree = MarkdownSyntaxTree.ParseText("The meeting is ~~Monday~~ Tuesday.\n", options);

var strikethrough = tree.GetRoot().DescendantNodes().OfType<MarkdownStrikethroughSyntax>().Single();
Assert.Equal("Monday", strikethrough.GetPlainText());
Assert.Equal("<p>The meeting is <del>Monday</del> Tuesday.</p>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

A [MarkdownStrikethroughSyntax](cref:Meziantou.Framework.Markdown.MarkdownStrikethroughSyntax) has its tildes as tokens,
[OpeningDelimiterToken](cref:Meziantou.Framework.Markdown.MarkdownStrikethroughSyntax.OpeningDelimiterToken) and
[ClosingDelimiterToken](cref:Meziantou.Framework.Markdown.MarkdownStrikethroughSyntax.ClosingDelimiterToken), as an
emphasis has its delimiters.

The tildes follow the rules of the delimiters of emphasis. Unlike cmark-gfm, the delimiters between the tildes of a
strikethrough are text, as they are in an emphasis, so that the constructs never overlap.

The [extra emphasis](09-EmphasisExtras.md) adds a subscript written with one tilde: when it is enabled, `~text~` is a
subscript rather than a strikethrough.
