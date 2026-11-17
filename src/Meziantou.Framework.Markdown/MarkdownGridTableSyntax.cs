using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Markdown.Parsing;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>
/// A grid table of Markdig: a line of columns, <c>+---+---+</c>, then lines that start with <c>|</c>, whose cells are separated
/// by <c>|</c>, and lines that start with <c>+</c>, which separate the rows. A cell can span columns and rows.
/// </summary>
/// <remarks>
/// The lines of the table are tokens: the cells are read from them, and the content of each cell is a document of its own,
/// parsed with the options of the tree, whose reference links can use the definitions of the tree.
/// </remarks>
public sealed class MarkdownGridTableSyntax : MarkdownBlockSyntax
{
    internal MarkdownGridTableSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    private (IReadOnlyList<MarkdownGridTableColumn> Columns, IReadOnlyList<MarkdownGridTableRow> Rows)? _layout;

    /// <summary>Gets the lines of the table, with their line endings.</summary>
    public IReadOnlyList<SyntaxToken> LineTokens => GetTokens(SyntaxKind.GridTableLineToken);

    /// <summary>Gets the columns of the table, from its first line.</summary>
    public IReadOnlyList<MarkdownGridTableColumn> Columns => GetLayout().Columns;

    /// <summary>Gets the rows of the table: the rows above a line of <c>=</c>, <c>+===+</c>, are the header.</summary>
    public IReadOnlyList<MarkdownGridTableRow> Rows => GetLayout().Rows;

    private (IReadOnlyList<MarkdownGridTableColumn> Columns, IReadOnlyList<MarkdownGridTableRow> Rows) GetLayout()
    {
        if (_layout is { } layout)
        {
            return layout;
        }

        var tokens = LineTokens;
        var lines = new List<string>(tokens.Count);
        foreach (var token in tokens)
        {
            lines.Add(token.Text.TrimEnd('\r', '\n'));
        }

        // The parser only makes a table of lines that are one, except at the end of a cell that is not closed
        var grid = GridTableLayout.Read(lines, force: true)!;
        var totalWidth = 0;
        foreach (var column in grid.Columns)
        {
            totalWidth += column.End - column.Start - 1;
        }

        var columns = grid.Columns.Select(column => new MarkdownGridTableColumn(column.Alignment, (float)(column.End - column.Start - 1) * 100.0f / totalWidth)).ToArray();
        var outer = SyntaxTree as MarkdownSyntaxTree;
        var depth = (outer?.CellDepth ?? 0) + Ancestors().Count(ancestor => ancestor is MarkdownBlockSyntax) + 1;
        var rows = grid.Rows.Select(row => new MarkdownGridTableRow(row.IsHeader, [.. row.Cells.Select(cell =>
        {
            var text = string.Join('\n', cell.Segments.Select(segment => lines[segment.Line][segment.Start..segment.End]));
            return new MarkdownGridTableCell(cell.ColumnIndex, cell.ColumnSpan, cell.RowSpan, text, outer, cell.IsClosed, cell.Segments.Count > 0 ? cell.Segments[0].Line : -1, depth);
        })], row.Line)).ToArray();
        _layout = (columns, rows);
        return _layout.Value;
    }

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownGridTable(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownGridTable(this);
    }
}
