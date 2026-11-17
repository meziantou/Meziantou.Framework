---
title: Tables
description: Write tables with a header row, a delimiter row that aligns the columns, and rows of cells, as GitHub does.
---

CommonMark has no tables: a grid of values must be written as HTML.
[Tables](cref:Meziantou.Framework.Markdown.MarkdownExtensions.Tables) adds the tables of GitHub Flavored Markdown, which
are readable as text and render as `<table>`. The first row is the header, the second one, the delimiter row, separates
it from the body and sets the alignment of each column with colons: `:--` aligns left, `--:` right, and `:-:` centers.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_Tables;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.Tables };
var tree = MarkdownSyntaxTree.ParseText("| Fruit | Price |\n| :---- | ----: |\n| Apple | 1 |\n| Pear |\n", options);
var table = Assert.IsType<MarkdownTableSyntax>(tree.GetRoot().Blocks.Single());

Assert.Equal([MarkdownTableAlignment.Left, MarkdownTableAlignment.Right], table.Alignments);
Assert.Equal("Fruit", table.HeaderRow!.Cells[0].GetPlainText());
Assert.Equal(["Apple", "1"], table.Rows[0].Cells.Select(cell => cell.GetPlainText()));

// The row with fewer cells than the header gets empty cells
Assert.Equal("<table>\n<thead>\n<tr>\n<th align=\"left\">Fruit</th>\n<th align=\"right\">Price</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td align=\"left\">Apple</td>\n<td align=\"right\">1</td>\n</tr>\n<tr>\n<td align=\"left\">Pear</td>\n<td align=\"right\"></td>\n</tr>\n</tbody>\n</table>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

## Syntax tree

A [MarkdownTableSyntax](cref:Meziantou.Framework.Markdown.MarkdownTableSyntax) has a
[HeaderRow](cref:Meziantou.Framework.Markdown.MarkdownTableSyntax.HeaderRow), a
[DelimiterRow](cref:Meziantou.Framework.Markdown.MarkdownTableSyntax.DelimiterRow) and its other
[Rows](cref:Meziantou.Framework.Markdown.MarkdownTableSyntax.Rows), and
[Alignments](cref:Meziantou.Framework.Markdown.MarkdownTableSyntax.Alignments) gives the alignment of each column. The
pipes and the cells of the delimiter row (`:--:`) are tokens of the rows, and a
[MarkdownTableCellSyntax](cref:Meziantou.Framework.Markdown.MarkdownTableCellSyntax) holds its inlines.

## Details

- In a cell, `\|` is a pipe, even in a code span, as GFM says.
- A row with fewer cells than the header is completed with empty cells, and the cells after the last column are
  ignored.
- As in cmark-gfm, once a table has added more than 524,288 empty cells, its next line is not a row but a paragraph, so
  that a short text cannot render as a huge table.

[Pipe tables](07-PipeTables.md) are a more permissive syntax, which replaces this one when both are enabled.
