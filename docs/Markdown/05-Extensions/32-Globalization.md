---
title: Right-to-left text
description: Mark the blocks and links written in Arabic, Hebrew or another right-to-left script with dir="rtl".
---

A browser lays out a block from left to right unless it has `dir="rtl"`, so text in Arabic or Hebrew is aligned on the
wrong side. [Globalization](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Globalization) makes the renderer add
`dir="rtl"` to the blocks and the links whose text starts with a right-to-left character, and `align="right"` to their
tables.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownHtmlRenderer_Globalization;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Globalization | MarkdownExtensions.PipeTables };
var tree = MarkdownSyntaxTree.ParseText("Hello\n\n> مرحبا *world*\n\n| الاسم |\n|-------|\n| x |\n", options);

Assert.Equal("<p>Hello</p>\n<blockquote dir=\"rtl\">\n<p dir=\"rtl\">مرحبا <em>world</em></p>\n</blockquote>\n<table dir=\"rtl\" align=\"right\">\n<thead>\n<tr>\n<th>الاسم</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td>x</td>\n</tr>\n</tbody>\n</table>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

## Details

- The direction is the one of the first character that has one: the digits, the punctuation and the whitespace have
  none, and a block takes the direction of its first child.
- The text of a construct that is not parsed, such as `]` or a lone `\`, is part of the text after it, but a delimiter
  of emphasis, a pipe of a pipe table or a bracket that a `]` closes without a link is not: `*مرحبا` is left-to-right.
- The syntax tree does not change.
