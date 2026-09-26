// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Parsers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Extensions.Tables;

/// <summary>
/// Defines a table that contains an optional <see cref="TableRow"/>.
/// </summary>
/// <seealso cref="ContainerBlock" />
public class Table : ContainerBlock
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Table"/> class.
    /// </summary>
    public Table() : base(null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Table"/> class.
    /// </summary>
    /// <param name="parser">The parser used to create this block.</param>
    public Table(BlockParser? parser) : base(parser)
    {
    }

    /// <summary>
    /// Gets or sets the column alignments. May be null.
    /// </summary>
    public List<TableColumnDefinition> ColumnDefinitions { get; } = new();

    /// <summary>
    /// Checks if the table structure is valid.
    /// </summary>
    /// <returns><c>True</c> if the table has rows and the number of cells per row is correct, other wise <c>false</c>.</returns>
    public bool IsValid()
    {
        // A table with no rows is not valid.
        if (Count == 0)
        {
            return false;
        }
        var columnCount = ColumnDefinitions.Count;
        var rows = new int[Count];
        for (int i = 0; i < Count; i++)
        {
            var row = (TableRow)this[i];
            for (int j = 0; j < row.Count; j++)
            {
                var cell = (TableCell)row[j];
                rows[i] += cell.ColumnSpan;
                var rowSpan = cell.RowSpan - 1;
                while (rowSpan > 0)
                {
                    if (i+rowSpan > (rows.Length-1))
                    {
                        return false;
                    }

                    rows[i + rowSpan] += cell.ColumnSpan;
                    rowSpan--;
                }
            }
            if (rows[i] > columnCount)
            {
                return false;
            }
        }
        return true;
    }

    // Same bound as cmark-gfm on the number of empty cells added to short rows. Without it, a row with many columns
    // followed by many short rows makes the table, and the HTML, grow quadratically with the size of the Markdown.
    internal const int MaximumAutocompletedCells = 0x80000;

    /// <summary>
    /// Normalizes the number of columns of this table by taking the maximum columns and appending empty cells.
    /// </summary>
    /// <remarks>
    /// Rows are left unchanged when more than 524,288 empty cells would be needed.
    /// </remarks>
    public void NormalizeUsingMaxWidth()
    {
        var maxColumn = 0;
        for (int i = 0; i < this.Count; i++)
        {
            if (this[i] is TableRow row && row.Count > maxColumn)
            {
                maxColumn = row.Count;
            }
        }

        if (GetMissingCellCount(maxColumn) > MaximumAutocompletedCells)
        {
            return;
        }

        for (int i = 0; i < this.Count; i++)
        {
            if (this[i] is TableRow row)
            {
                for (int j = row.Count; j < maxColumn; j++)
                {
                    row.Add(new TableCell());
                }
            }
        }
    }

    private long GetMissingCellCount(int columnCount)
    {
        long count = 0;
        for (int i = 0; i < this.Count; i++)
        {
            if (this[i] is TableRow row && row.Count < columnCount)
            {
                count += columnCount - row.Count;
            }
        }

        return count;
    }

    /// <summary>
    /// Normalizes the number of columns of this table by taking the amount of columns defined in the header
    /// and appending empty cells or removing extra cells as needed.
    /// </summary>
    /// <remarks>
    /// Short rows are not completed when more than 524,288 empty cells would be needed.
    /// </remarks>
    public void NormalizeUsingHeaderRow()
    {
        if (this.Count == 0)
        {
            return;
        }

        var maxColumn = 0;

        var headerRow = this[0] as TableRow;
        if (headerRow != null)
        {
            maxColumn = headerRow.Count;
        }

        var completeShortRows = GetMissingCellCount(maxColumn) <= MaximumAutocompletedCells;
        for (int i = 0; i < this.Count; i++)
        {
            if (this[i] is TableRow row)
            {
                for (int j = row.Count; completeShortRows && j < maxColumn; j++)
                {
                    row.Add(new TableCell());
                }

                for (int j = maxColumn; j < row.Count; j++)
                {
                    row.RemoveAt(j);
                }
            }
        }
    }
}