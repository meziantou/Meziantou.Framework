---
title: Grid tables
description: Write tables whose cells span several lines or columns and hold blocks, such as lists or code.
---

The cells of a pipe table are one line of inlines.
[GridTables](cref:Meziantou.Framework.Markdown.MarkdownExtensions.GridTables) draws the table with `+`, `-` and `|`, so
that a cell can span several lines and several columns, and hold blocks such as paragraphs, lists or code blocks.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxTree_GridTables;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.GridTables };
var tree = MarkdownSyntaxTree.ParseText("+-------+:------:+\n| Name  | Price  |\n+=======+========+\n| Apple | 1      |\n|       | each   |\n+-------+--------+\n| - Pear         |\n+-------+--------+\n", options);
var table = Assert.IsType<MarkdownGridTableSyntax>(tree.GetRoot().Blocks.Single());

Assert.Equal([MarkdownTableAlignment.None, MarkdownTableAlignment.Center], table.Columns.Select(column => column.Alignment));
Assert.True(table.Rows[0].IsHeader);

// The text of a cell is its lines between its pipes, which is a document of its own
var price = table.Rows[1].Cells[1];
Assert.Equal(" 1\n each", price.Text);
Assert.Equal("1\neach", price.Content.GetRoot().Blocks.Single().GetPlainText());
var pear = table.Rows[2].Cells.Single();
Assert.Equal(2, pear.ColumnSpan);
Assert.IsType<MarkdownListSyntax>(pear.Content.GetRoot().Blocks.Single());
```
<!-- endref -->

## Syntax

- A line of columns, `+---+:--:+`, starts the table: the dashes of each column give its width, and the colons its
  alignment.
- The next lines start with `|`, which separates the cells, or with `+`, which separates the rows: a cell that a `+`
  line does not cross continues in the next row.
- The rows above a line of `=` are the header.
- A cell spans the columns up to its next `|`.
- When the lines are not a table, they are a paragraph.

## Syntax tree

A [MarkdownGridTableSyntax](cref:Meziantou.Framework.Markdown.MarkdownGridTableSyntax) has the lines as tokens, and
reads its [Columns](cref:Meziantou.Framework.Markdown.MarkdownGridTableSyntax.Columns) and its
[Rows](cref:Meziantou.Framework.Markdown.MarkdownGridTableSyntax.Rows) from them. The content of a cell is a document of
its own, [MarkdownGridTableCell.Content](cref:Meziantou.Framework.Markdown.MarkdownGridTableCell.Content), parsed with
the options of the tree: its reference links can use the definitions of the tree, but not its footnotes or its
abbreviations.
