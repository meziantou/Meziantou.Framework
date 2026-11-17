namespace Meziantou.Framework.Markdown;

/// <summary>A row of a grid table.</summary>
public sealed class MarkdownGridTableRow
{
    internal MarkdownGridTableRow(bool isHeader, IReadOnlyList<MarkdownGridTableCell> cells, int lineIndex)
    {
        IsHeader = isHeader;
        Cells = cells;
        LineIndex = lineIndex;
    }

    // The index of the line where Markdig starts the row in the lines of the table, or -1
    internal int LineIndex { get; }

    /// <summary>Gets a value indicating whether the row is above a line of <c>=</c>.</summary>
    public bool IsHeader { get; }

    /// <summary>Gets the cells that start in the row. A cell of a row above can span it.</summary>
    public IReadOnlyList<MarkdownGridTableCell> Cells { get; }
}
