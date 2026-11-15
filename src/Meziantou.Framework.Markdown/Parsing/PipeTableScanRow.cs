namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>A row of a pipe table: its pipes, and its cells without the whitespace around them, or the cells of the delimiter row that are not empty.</summary>
internal sealed class PipeTableScanRow
{
    // Where the row starts and ends, without the line break after it
    public int Start { get; set; }

    public int End { get; set; }

    public List<int> Pipes { get; } = [];

    public List<(int Start, int End)> Cells { get; } = [];

    // The generic attributes of the row that are not in a cell: the ones of the delimiter row, or the ones that are all the text
    // before the first pipe or after the last one
    public List<(int Start, int End)> Attributes { get; } = [];
}
