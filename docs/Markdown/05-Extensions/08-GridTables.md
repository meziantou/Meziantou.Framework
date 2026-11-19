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
the options of the tree. It is part of the document for what it refers to and for the identifiers it gets:

- Its reference links can use the definitions of the tree, but not its abbreviations.
- Its footnote references are to the footnotes of the tree, and are numbered where the table is. A cell has no footnote
  definition and no YAML front matter: `[^1]: text` in a cell is a link reference definition, as in a block quote.
- The identifiers of its headings, with
  [AutoIdentifiers](cref:Meziantou.Framework.Markdown.MarkdownExtensions.AutoIdentifiers), are unique among the ones of the
  document.
- Its block quotes are nested in the table: they are alerts only when
  [AllowNestedAlerts](cref:Meziantou.Framework.Markdown.MarkdownParseOptions.AllowNestedAlerts) is set.

## Reaching the content of the cells

The nodes of the content of a cell are not descendants of the table:
[DescendantNodes](cref:Meziantou.Framework.Language.SyntaxNode.DescendantNodes(System.Func{Meziantou.Framework.Language.SyntaxNode,System.Boolean})),
[MarkdownSyntaxRewriter](cref:Meziantou.Framework.Markdown.MarkdownSyntaxRewriter) and the edit methods of the tree do not
reach them. To visit them, create a [MarkdownSyntaxWalker](cref:Meziantou.Framework.Markdown.MarkdownSyntaxWalker) with
`visitGridTableCells: true`: after the children of a grid table, it visits the document of each of its cells, row by row.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxWalker_LinkCollector;format=md-fenced-code-block;dedent=true -->
```csharp
// Finds the links of a document, the ones of the content of its grid table cells included
private sealed class LinkCollector() : MarkdownSyntaxWalker(SyntaxWalkerDepth.Node, visitGridTableCells: true)
{
    public List<MarkdownLinkSyntax> Links { get; } = [];

    public override void VisitMarkdownLink(MarkdownLinkSyntax node)
    {
        Links.Add(node);
        base.VisitMarkdownLink(node);
    }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownSyntaxWalker_GridTableCells;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.GridTables };
var tree = MarkdownSyntaxTree.ParseText("[a](/u)\n\n+---------+---------+\n| [b](/v) | [c](/w) |\n+---------+---------+\n", options);

var collector = new LinkCollector();
collector.Visit(tree.GetRoot());

Assert.Equal(["/u", "/v", "/w"], collector.Links.Select(link => link.Destination));
```
<!-- endref -->

The positions of these nodes are in the text of the cell.
[MapSpan](cref:Meziantou.Framework.Markdown.MarkdownGridTableCell.MapSpan(Meziantou.Framework.Language.TextSpan)) and
[MapPosition](cref:Meziantou.Framework.Markdown.MarkdownGridTableCell.MapPosition(System.Int32)) give the positions in
the text of the tree of the table, for example to report a diagnostic. A span on several lines of the cell also covers
the borders between them.

<!-- ref:../../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs;region=MarkdownGridTableCell_MapSpan;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.GridTables };
var text = "Intro\n\n+---------------+\n| See [a](/u)   |\n| and [b](/v)   |\n+---------------+\n";
var tree = MarkdownSyntaxTree.ParseText(text, options);
var cell = tree.GetRoot().Blocks.OfType<MarkdownGridTableSyntax>().Single().Rows[0].Cells[0];

// The nodes of the content of the cell are not in the tree, and their spans are in the text of the cell
Assert.Empty(tree.GetRoot().DescendantNodes().OfType<MarkdownLinkSyntax>());
var link = cell.Content.GetRoot().DescendantNodes().OfType<MarkdownLinkSyntax>().Last();
Assert.Equal("[b](/v)", cell.Text.Substring(link.Span.Start, link.Span.Length));

// MapSpan gives the span of the link in the text of the tree
var span = cell.MapSpan(link.Span);
Assert.Equal("[b](/v)", text.Substring(span.Start, span.Length));
```
<!-- endref -->

To change the content of a cell, replace the text of the table: a rewriter can return a new grid table for it.
