---
title: CJK-friendly emphasis
description: Let emphasis open and close next to Chinese, Japanese and Korean punctuation, which CommonMark does not.
---

Chinese, Japanese and Korean do not put spaces between words, so the rules of CommonMark, which look at the characters
around the delimiters, often leave `**` as text.
[CjkFriendlyEmphasis](cref:Meziantou.Framework.Markdown.MarkdownExtensions.CjkFriendlyEmphasis) implements the
[CJK-friendly amendment](https://github.com/tats-u/markdown-cjk-friendly) of these rules: a Chinese, Japanese or Korean
character next to the delimiters counts as whitespace, so the delimiters can open or close.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_CjkFriendlyEmphasis;format=md-fenced-code-block;dedent=true -->
```csharp
// CommonMark does not close the emphasis: the punctuation before ** is followed by a letter
Assert.Equal("<p>**テスト。**です</p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText("**テスト。**です\n")));

var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.CjkFriendlyEmphasis };
Assert.Equal("<p><strong>テスト。</strong>です</p>\n", MarkdownHtmlRenderer.ToHtml(MarkdownSyntaxTree.ParseText("**テスト。**です\n", options)));
```
<!-- endref -->

CommonMark does not close `**テスト。**です`, as the punctuation before the delimiters is followed by a letter. The extension
does.
