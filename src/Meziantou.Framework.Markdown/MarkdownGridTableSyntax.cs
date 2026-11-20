using System.Collections.Immutable;
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
/// parsed with the options of the tree, whose reference links can use the definitions of the tree, and whose footnote
/// references are to the footnotes of the tree. The nodes of the content of the cells are not descendants of the table:
/// <see cref="SyntaxNode.DescendantNodes(Func{SyntaxNode, bool}?)"/> and <see cref="MarkdownSyntaxRewriter"/> do not reach
/// them. <see cref="MarkdownSyntaxWalker"/> visits them when it is created with <c>visitGridTableCells</c>, and
/// <see cref="MarkdownGridTableCell.MapSpan(TextSpan)"/> gives the span in the text of the tree of a span of the content of a
/// cell.
/// </remarks>
public sealed class MarkdownGridTableSyntax : MarkdownBlockSyntax
{
    internal MarkdownGridTableSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    private Layout? _layout;

    /// <summary>Gets the lines of the table, with their line endings.</summary>
    public ImmutableArray<SyntaxToken> LineTokens => GetTokens(SyntaxKind.GridTableLineToken);

    /// <summary>Gets the columns of the table, from its first line.</summary>
    public ImmutableArray<MarkdownGridTableColumn> Columns => GetLayout().Columns;

    /// <summary>Gets the rows of the table: the rows above a line of <c>=</c>, <c>+===+</c>, are the header.</summary>
    public ImmutableArray<MarkdownGridTableRow> Rows => GetLayout().Rows;

    private Layout GetLayout()
    {
        if (_layout is { } layout)
        {
            return layout;
        }

        var tokens = LineTokens;
        var lines = new List<string>(tokens.Length);
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

        var columns = grid.Columns.Select(column => new MarkdownGridTableColumn(column.Alignment, (float)(column.End - column.Start - 1) * 100.0f / totalWidth)).ToImmutableArray();
        var outer = SyntaxTree as MarkdownSyntaxTree;
        var depth = (outer?.CellDepth ?? 0) + Ancestors().Count(ancestor => ancestor is MarkdownBlockSyntax) + 1;
        var rows = grid.Rows.Select(row => new MarkdownGridTableRow(row.IsHeader, [.. row.Cells.Select(cell =>
        {
            var text = string.Join('\n', cell.Segments.Select(segment => lines[segment.Line][segment.Start..segment.End]));
            TextSpan[] segments = [.. cell.Segments.Select(segment => new TextSpan(tokens[segment.Line].SpanStart + segment.Start, segment.End - segment.Start))];
            return new MarkdownGridTableCell(cell.ColumnIndex, cell.ColumnSpan, cell.RowSpan, text, outer, cell.IsClosed, cell.Segments.Count > 0 ? cell.Segments[0].Line : -1, depth, segments);
        })], row.Line)).ToImmutableArray();
        layout = new Layout(columns, rows);
        _layout = layout;
        return layout;
    }

    // The node and the nodes below it in the order of the text, with, after each grid table, the nodes of the content of its
    // cells, row by row: the nodes of the document that Markdig reads, as it reads the cells as blocks of the document
    internal static IEnumerable<SyntaxNode> DescendantNodesAndSelfAndCellNodes(SyntaxNode node)
    {
        foreach (var descendant in node.DescendantNodesAndSelf())
        {
            yield return descendant;
            if (descendant is MarkdownGridTableSyntax table)
            {
                foreach (var row in table.Rows)
                {
                    foreach (var cell in row.Cells)
                    {
                        foreach (var cellNode in DescendantNodesAndSelfAndCellNodes(cell.Content.GetRoot()))
                        {
                            yield return cellNode;
                        }
                    }
                }
            }
        }
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

    private sealed record Layout(ImmutableArray<MarkdownGridTableColumn> Columns, ImmutableArray<MarkdownGridTableRow> Rows);
}
