---
title: Pipe tables
description: Write tables whose rows need no pipe around them, with options for the header row and the widths of the columns.
---

[PipeTables](cref:Meziantou.Framework.Markdown.MarkdownExtensions.PipeTables) is a more permissive syntax of tables than
the [tables of GFM](02-Tables.md), which it replaces when both are enabled. The lines of a paragraph that have a pipe
are a table when the second one is a delimiter row: a pipe or a line break ends a cell, so the rows can have no pipe
around them, and the cells of the delimiter row can be empty.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_PipeTables;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.PipeTables };
var tree = MarkdownSyntaxTree.ParseText("Fruits:\n| Name | Price |\n|:-----|------:|\n| Apple | 1 | each\n", options);
var table = tree.GetRoot().Blocks.OfType<MarkdownTableSyntax>().Single();

Assert.True(table.IsPipeTable);
Assert.Equal([MarkdownTableAlignment.Left, MarkdownTableAlignment.Right], table.Alignments);
Assert.Equal(["Apple", "1", "each"], table.Rows[0].Cells.Select(cell => cell.GetPlainText()));
Assert.Equal("<p>Fruits:</p>\n<table>\n<thead>\n<tr>\n<th align=\"left\">Name</th>\n<th align=\"right\">Price</th>\n<th align=\"right\"></th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td align=\"left\">Apple</td>\n<td align=\"right\">1</td>\n<td align=\"right\">each</td>\n</tr>\n</tbody>\n</table>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->

## Details

- The pipes of a code span, of a link or of HTML are text.
- A line without a pipe ends the table: the lines before and after the table are paragraphs.
- The table has as many columns as its longest row has cells, and a column after the last cell of the delimiter row has
  its alignment.
- [MarkdownTableSyntax.IsPipeTable](cref:Meziantou.Framework.Markdown.MarkdownTableSyntax.IsPipeTable) tells them from
  the tables of GFM, and the [generic attributes](15-GenericAttributes.md) of their rows are for the table.
- The renderer adds at most 524,288 empty cells to the short rows of a document: the rows of a table that would exceed
  the limit keep their cells.

## Options

[PipeTableOptions](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.PipeTableOptions) changes how the tables are
read:

- [RequireHeaderSeparator](cref:Meziantou.Framework.Markdown.MarkdownPipeTableOptions.RequireHeaderSeparator) set to
  `false` reads the lines that have a pipe, up to the end of their paragraph, as a table without a header row:
  [HeaderRow](cref:Meziantou.Framework.Markdown.MarkdownTableSyntax.HeaderRow) and
  [DelimiterRow](cref:Meziantou.Framework.Markdown.MarkdownTableSyntax.DelimiterRow) are `null`.
- [UseHeaderForColumnCount](cref:Meziantou.Framework.Markdown.MarkdownPipeTableOptions.UseHeaderForColumnCount) gives
  the table as many columns as its first row has cells.
- [InferColumnWidthsFromSeparator](cref:Meziantou.Framework.Markdown.MarkdownPipeTableOptions.InferColumnWidthsFromSeparator)
  writes the width of the columns, in proportion to their dashes.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_PipeTableOptions;format=md-fenced-code-block;dedent=true -->
```csharp
// The first row gives the number of columns, and the dashes the widths of the columns
var options = new MarkdownParseOptions
{
    Extensions = MarkdownExtensions.PipeTables,
    PipeTableOptions = new MarkdownPipeTableOptions { UseHeaderForColumnCount = true, InferColumnWidthsFromSeparator = true },
};
var tree = MarkdownSyntaxTree.ParseText("Name | Price\n:---|------:\nApple | 1 | each\n", options);

Assert.Equal("<table>\n<col style=\"width:33.33%\" />\n<col style=\"width:66.67%\" />\n<thead>\n<tr>\n<th align=\"left\">Name</th>\n<th align=\"right\">Price</th>\n</tr>\n</thead>\n<tbody>\n<tr>\n<td align=\"left\">Apple</td>\n<td align=\"right\">1</td>\n</tr>\n</tbody>\n</table>\n", MarkdownHtmlRenderer.ToHtml(tree));

// A table without a delimiter row has no header row
options = new MarkdownParseOptions
{
    Extensions = MarkdownExtensions.PipeTables,
    PipeTableOptions = new MarkdownPipeTableOptions { RequireHeaderSeparator = false },
};
tree = MarkdownSyntaxTree.ParseText("a | b\nc | d\n", options);
var table = tree.GetRoot().Blocks.OfType<MarkdownTableSyntax>().Single();

Assert.Null(table.HeaderRow);
Assert.Null(table.DelimiterRow);
Assert.HasCount(2, table.Rows);
Assert.Equal("<table>\n<tbody>\n<tr>\n<td>a</td>\n<td>b</td>\n</tr>\n<tr>\n<td>c</td>\n<td>d</td>\n</tr>\n</tbody>\n</table>\n", MarkdownHtmlRenderer.ToHtml(tree));
```
<!-- endref -->
