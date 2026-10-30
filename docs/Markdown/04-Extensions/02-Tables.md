---
title: Tables
description: Render GitHub pipe tables and Pandoc grid tables, align columns, follow the GitHub rules and read tables from the tree.
---

## Pipe tables

[UsePipeTables](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UsePipeTables) enables the tables of GitHub: a header row, a delimiter row of dashes, and body rows, with `|` between
the cells. A `:` in the delimiter row aligns the column to the left (`:--`), to the right (`--:`) or to the center
(`:-:`):

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestPipeTable.cs;region=MarkdownExtensions_UsePipeTables;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UsePipeTables()
    .Build();

var html = MarkdownConverter.ToHtml("""
    | Name | Age |
    |:-----|----:|
    | Ann  | 32  |
    """, pipeline);
Assert.Equal("""
    <table>
    <thead>
    <tr>
    <th style="text-align: left;">Name</th>
    <th style="text-align: right;">Age</th>
    </tr>
    </thead>
    <tbody>
    <tr>
    <td style="text-align: left;">Ann</td>
    <td style="text-align: right;">32</td>
    </tr>
    </tbody>
    </table>

    """.ReplaceLineEndings("\n"), html);
```
<!-- endref -->

The `|` at the start and at the end of the lines are optional. Write `\|` for a `|` inside a cell.

## Follow the GitHub rules

By default, the parser is permissive: a row with more cells than the header adds columns to the table. With
[UseGfmRules](cref:Meziantou.Framework.Markdown.Extensions.Tables.PipeTableOptions.UseGfmRules), the table follows the [GitHub Flavored Markdown](https://github.github.com/gfm/#tables-extension-)
specification, so it renders as on GitHub:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestPipeTable.cs;region=PipeTableOptions_UseGfmRules;format=md-fenced-code-block;dedent=true -->
```csharp
// The body row has more cells than the header
var markdown = "a | b\n--|--\n1 | 2 | 3";

var permissive = MarkdownConverter.ToHtml(markdown, new MarkdownPipelineBuilder().UsePipeTables().Build());
Assert.Contains("<td>3</td>", permissive); // A third column is added

var gfm = MarkdownConverter.ToHtml(markdown, new MarkdownPipelineBuilder().UsePipeTables(new PipeTableOptions { UseGfmRules = true }).Build());
Assert.DoesNotContain("<td>3</td>", gfm); // The extra cell is dropped, as on GitHub
```
<!-- endref -->

The options of [PipeTableOptions](cref:Meziantou.Framework.Markdown.Extensions.Tables.PipeTableOptions):

| Option | Default | Effect |
| --- | --- | --- |
| [UseGfmRules](cref:Meziantou.Framework.Markdown.Extensions.Tables.PipeTableOptions.UseGfmRules) | `false` | Follows the GFM rules: a delimiter for every header cell, the same number of cells in the header and the delimiter row, and body rows padded or truncated to the width of the header. Overrides [RequireHeaderSeparator](cref:Meziantou.Framework.Markdown.Extensions.Tables.PipeTableOptions.RequireHeaderSeparator) and [UseHeaderForColumnCount](cref:Meziantou.Framework.Markdown.Extensions.Tables.PipeTableOptions.UseHeaderForColumnCount) |
| [RequireHeaderSeparator](cref:Meziantou.Framework.Markdown.Extensions.Tables.PipeTableOptions.RequireHeaderSeparator) | `true` | Requires the delimiter row. Without it, a table can have no header |
| [UseHeaderForColumnCount](cref:Meziantou.Framework.Markdown.Extensions.Tables.PipeTableOptions.UseHeaderForColumnCount) | `false` | Uses the header to count the columns: shorter rows get empty cells and the extra cells are removed. By default, the longest row gives the number of columns |
| [InferColumnWidthsFromSeparator](cref:Meziantou.Framework.Markdown.Extensions.Tables.PipeTableOptions.InferColumnWidthsFromSeparator) | `false` | Gives each column a width proportional to the number of dashes of its delimiter |

In [`Configure`](01-Overview.md#enable-extensions-from-a-configuration-string), `gfm-pipetables` enables pipe tables with
[UseGfmRules](cref:Meziantou.Framework.Markdown.Extensions.Tables.PipeTableOptions.UseGfmRules).

## Grid tables

[UseGridTables](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseGridTables) enables the grid tables of Pandoc, drawn with `+`, `-` and `|`. A row of `=` separates the header from
the body. The width of each column is proportional to its number of dashes:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_UseGridTables;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UseGridTables()
    .Build();

var html = MarkdownConverter.ToHtml("""
    +--------+--------+
    | Name   | Age    |
    +========+========+
    | Ann    | 32     |
    +--------+--------+
    """, pipeline);
Assert.Equal("""
    <table>
    <col style="width:50%" />
    <col style="width:50%" />
    <thead>
    <tr>
    <th>Name</th>
    <th>Age</th>
    </tr>
    </thead>
    <tbody>
    <tr>
    <td>Ann</td>
    <td>32</td>
    </tr>
    </tbody>
    </table>

    """.ReplaceLineEndings("\n"), html);
```
<!-- endref -->

Unlike pipe tables, the cells of a grid table can contain several lines and blocks, such as lists, and span several
columns or rows.

## Bootstrap classes

[UseBootstrap](cref:Meziantou.Framework.Markdown.MarkdownExtensions.UseBootstrap) adds the classes of the [Bootstrap](https://getbootstrap.com/) framework: `table` on tables, `blockquote`
on quotes, `img-fluid` on images, `figure` and `figure-caption` on [figures](05-Blocks.md#figures-footers-and-citations),
and `alert` with `alert-primary`, `alert-warning`… on [alerts](05-Blocks.md#alerts):

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestCoreApi.cs;region=MarkdownExtensions_UseBootstrap;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder()
    .UsePipeTables()
    .UseBootstrap()
    .Build();

var html = MarkdownConverter.ToHtml("> A quote\n\n![Logo](logo.png)", pipeline);
Assert.Equal("<blockquote class=\"blockquote\">\n<p>A quote</p>\n</blockquote>\n<p><img src=\"logo.png\" class=\"img-fluid\" alt=\"Logo\" /></p>\n", html);
```
<!-- endref -->

## Read a table from the tree

A table is a [Table](cref:Meziantou.Framework.Markdown.Extensions.Tables.Table) block (namespace [Meziantou.Framework.Markdown.Extensions.Tables](cref:Meziantou.Framework.Markdown.Extensions.Tables)) that contains [TableRow](cref:Meziantou.Framework.Markdown.Extensions.Tables.TableRow) blocks,
which contain [TableCell](cref:Meziantou.Framework.Markdown.Extensions.Tables.TableCell) blocks. [ColumnDefinitions](cref:Meziantou.Framework.Markdown.Extensions.Tables.Table.ColumnDefinitions) gives the alignment of each column:

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/TestPipeTable.cs;region=Table_SyntaxTree;format=md-fenced-code-block;dedent=true -->
```csharp
var pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();
var document = MarkdownConverter.Parse("| Name | Age |\n|:-----|----:|\n| Ann  | 32  |\n| Bob  | 41  |", pipeline);

var table = document.Descendants<Table>().Single();
Assert.Equal(TableColumnAlign.Right, table.ColumnDefinitions[1].Alignment);

var rows = table.Cast<TableRow>().ToList();
Assert.True(rows[0].IsHeader);

// A cell is a container block: its content is a paragraph
var cell = (TableCell)rows[2][0];
var text = ((ParagraphBlock)cell[0]).Inline!.FirstChild!.ToString();
Assert.Equal("Bob", text);
```
<!-- endref -->

See [The syntax tree](../05-SyntaxTree.md) to walk the other elements.
