---
title: Extra emphasis
description: Write subscript, superscript, inserted and marked text, such as H~2~O, 2^10^, ++text++ and ==text==.
---

CommonMark has two kinds of emphasis, `*` and `**`.
[EmphasisExtras](cref:Meziantou.Framework.Markdown.MarkdownExtensions.EmphasisExtras) adds the styles that chemical
formulas, exponents and reviews need:

| Syntax | Flag | HTML |
| ------ | ---- | ---- |
| `H~2~O` | [Subscript](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Subscript) | `<sub>` |
| `2^10^` | [Superscript](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Superscript) | `<sup>` |
| `++text++` | [Inserted](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Inserted) | `<ins>` |
| `==text==` | [Marked](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Marked) | `<mark>` |
| `~~text~~` | [Strikethrough](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Strikethrough) | `<del>` |

[EmphasisExtras](cref:Meziantou.Framework.Markdown.MarkdownExtensions.EmphasisExtras) enables the five flags, and each
one can be enabled alone.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_EmphasisExtras;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.EmphasisExtras };
var tree = MarkdownSyntaxTree.ParseText("H~2~O, 2^10^, ++added++, ==marked== and ~~removed~~\n", options);

var styles = tree.GetRoot().DescendantNodes().OfType<MarkdownEmphasisExtraSyntax>().Select(node => node.Style);
Assert.Equal([MarkdownEmphasisExtraStyle.Subscript, MarkdownEmphasisExtraStyle.Superscript, MarkdownEmphasisExtraStyle.Inserted, MarkdownEmphasisExtraStyle.Marked], styles);
Assert.Equal("<p>H<sub>2</sub>O, 2<sup>10</sup>, <ins>added</ins>, <mark>marked</mark> and <del>removed</del></p>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

They are [MarkdownEmphasisExtraSyntax](cref:Meziantou.Framework.Markdown.MarkdownEmphasisExtraSyntax) nodes, whose
[Style](cref:Meziantou.Framework.Markdown.MarkdownEmphasisExtraSyntax.Style) gives the kind. The delimiters follow the
rules of the delimiters of emphasis. When subscript is enabled, a single `~` is a subscript rather than a
[strikethrough](03-Strikethrough.md).
