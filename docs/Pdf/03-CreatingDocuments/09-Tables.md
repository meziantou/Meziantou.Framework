---
title: Tables
description: Lay out tables with fixed, relative and automatic columns, borders and backgrounds, and continue them on the next pages with their header rows.
---

[DrawTable](cref:Meziantou.Framework.Pdf.PdfContentBuilder.DrawTable) draws a [PdfTable](cref:Meziantou.Framework.Pdf.PdfTable) in a rectangle: the rows that fit in its height, with the texts of the cells
broken into lines that fit their columns. The result tells whether the table is complete; pass it to the next call to
continue the table on another page:

<!-- ref:../../../tests/Meziantou.Framework.Pdf.Tests/PdfContentBuilderTests.cs;region=Pdf_Table;format=md-fenced-code-block;dedent=true -->
```csharp
var document = new PdfDocument();
var bold = PdfFont.GetStandardFont(PdfStandardFont.HelveticaBold);
var table = new PdfTable(
    new PdfTableColumn(PdfTableColumnWidth.Relative()), // the rest of the width
    new PdfTableColumn(PdfTableColumnWidth.Auto, PdfTextAlignment.Right), // the width of the widest cell
    new PdfTableColumn(PdfTableColumnWidth.Fixed(80), PdfTextAlignment.Right))
{
    FontSize = 10,
    CellPadding = 5,
    BorderColor = PdfColor.FromGray(0.6),
};

// The header rows are drawn again at the top of each page
var header = table.AddHeaderRow("Description", "Quantity", "Amount");
header.Font = bold;
header.Background = PdfColor.FromGray(0.9);

for (var i = 1; i <= 80; i++)
{
    table.AddRow($"Item {i}: the text of a cell is broken into lines that fit the width of its column", "1", "10.00 €");
}

var total = table.AddRow("Total", "", "800.00 €");
total.Font = bold;
total.Cells[0].ColumnSpan = 2;
total.Cells.RemoveAt(1);

// Draw the rows that fit in the area of the page, then continue on new pages
var area = new PdfRectangle(50, 50, PdfSize.A4.Width - 50, PdfSize.A4.Height - 50);
var result = document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawTable(table, area);
while (!result.IsComplete)
{
    result = document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawTable(table, area, result);
}

document.Save(outputPath);
```
<!-- endref -->

## Columns

Each [PdfTableColumn](cref:Meziantou.Framework.Pdf.PdfTableColumn) has a width and the alignment of its cells. [GetColumnWidths](cref:Meziantou.Framework.Pdf.PdfTable.GetColumnWidths) returns the widths the
table gets in a width.

| Width | Effect |
| --- | --- |
| [PdfTableColumnWidth.Fixed](cref:Meziantou.Framework.Pdf.PdfTableColumnWidth.Fixed) | A width in points |
| [PdfTableColumnWidth.Auto](cref:Meziantou.Framework.Pdf.PdfTableColumnWidth.Auto) | The width of the widest paragraph of the cells of the column. When the table is too wide, the automatic columns get narrower, down to their widest word |
| [PdfTableColumnWidth.Relative](cref:Meziantou.Framework.Pdf.PdfTableColumnWidth.Relative) | A share of the width left by the other columns, in proportion to its weight: a column of weight 2 is twice as wide as a column of weight 1 |

A table without relative columns is as wide as its columns, which can be less than the rectangle: add a relative
column to fill it. The cells that span several columns ([ColumnSpan](cref:Meziantou.Framework.Pdf.PdfTableCell.ColumnSpan)) do not count in the automatic widths.

## Rows and cells

[AddRow](cref:Meziantou.Framework.Pdf.PdfTable.AddRow) adds a row from the texts of its cells, one per column; the columns after the last cell are empty.
[AddHeaderRow](cref:Meziantou.Framework.Pdf.PdfTable.AddHeaderRow) adds a header row and increments [HeaderRowCount](cref:Meziantou.Framework.Pdf.PdfTable.HeaderRowCount): the header rows are the first rows of the
table, drawn again at the top of each continuation.

The style of a cell comes from the cell, then its row, then the table:

| Property | Table | Row | Cell |
| --- | --- | --- | --- |
| Font and font size | [Font](cref:Meziantou.Framework.Pdf.PdfTable.Font) (Helvetica), [FontSize](cref:Meziantou.Framework.Pdf.PdfTable.FontSize) (10) | [Font](cref:Meziantou.Framework.Pdf.PdfTableRow.Font), [FontSize](cref:Meziantou.Framework.Pdf.PdfTableRow.FontSize) | [Font](cref:Meziantou.Framework.Pdf.PdfTableCell.Font), [FontSize](cref:Meziantou.Framework.Pdf.PdfTableCell.FontSize) |
| Text color | [TextColor](cref:Meziantou.Framework.Pdf.PdfTable.TextColor) | [TextColor](cref:Meziantou.Framework.Pdf.PdfTableRow.TextColor) | [TextColor](cref:Meziantou.Framework.Pdf.PdfTableCell.TextColor) |
| Background | | [Background](cref:Meziantou.Framework.Pdf.PdfTableRow.Background) | [Background](cref:Meziantou.Framework.Pdf.PdfTableCell.Background) |
| Alignment | | | [Alignment](cref:Meziantou.Framework.Pdf.PdfTableCell.Alignment), or the alignment of the column |

The table also sets the space between the borders and the text ([CellPadding](cref:Meziantou.Framework.Pdf.PdfTable.CellPadding), 4 points), the borders
([BorderWidth](cref:Meziantou.Framework.Pdf.PdfTable.BorderWidth), 0.5 point, 0 for none, and [BorderColor](cref:Meziantou.Framework.Pdf.PdfTable.BorderColor)), the distance between the lines of a cell
([LineHeight](cref:Meziantou.Framework.Pdf.PdfTable.LineHeight), 1.2 times the font size) and the [Hyphenator](cref:Meziantou.Framework.Pdf.PdfTable.Hyphenator) of the words of the cells. The texts of
the cells are broken into lines like the texts of a text box: see [Wrap a paragraph](03-Text.md#wrap-a-paragraph).

## Page breaks

- A row that does not fit in the height left is drawn at the top of the next rectangle, after the header rows. A row
  taller than a whole rectangle is split: the lines of its cells that fit are drawn, and the others continue in the next
  rectangle.
- When the first rectangle has no room for a row after the header rows (a table that starts at the bottom of a page),
  nothing is drawn and [IsComplete](cref:Meziantou.Framework.Pdf.PdfTableDrawResult.IsComplete) is `false`: the table starts in the next rectangle. A rectangle too small for
  the header rows and a line of the next row throws `InvalidOperationException`.
- [Bottom](cref:Meziantou.Framework.Pdf.PdfTableDrawResult.Bottom) is the y coordinate of the bottom of the part drawn, to draw what follows the table below it.
- The rows are laid out before anything is drawn: a character a font cannot show throws `ArgumentException` and leaves
  the content of the page unchanged.
