namespace Meziantou.Framework.Markdown;

/// <summary>A cell of a grid table: its text is a document of its own.</summary>
/// <remarks>
/// The positions of the nodes of <see cref="Content"/> are in the text of the cell: <see cref="MapPosition(int)"/> and
/// <see cref="MapSpan(TextSpan)"/> give the positions in the text of the tree of the table.
/// </remarks>
public sealed class MarkdownGridTableCell
{
    private readonly MarkdownSyntaxTree? _table;
    private MarkdownSyntaxTree? _content;

    private readonly bool _isClosed;

    // The blocks that the content is in: the table, and its containers in its tree and the trees around it
    private readonly int _depth;

    // The text of each line of the cell, in the text of the tree of the table
    private readonly TextSpan[] _segments;

    internal MarkdownGridTableCell(int columnIndex, int columnSpan, int rowSpan, string text, MarkdownSyntaxTree? table, bool isClosed, int lineIndex, int depth, TextSpan[] segments)
    {
        _segments = segments;
        _depth = depth;
        LineIndex = lineIndex;
        _isClosed = isClosed;
        ColumnIndex = columnIndex;
        ColumnSpan = columnSpan;
        RowSpan = rowSpan;
        Text = text;
        _table = table;
    }

    // The index of the first line of the cell in the lines of the table, or -1
    internal int LineIndex { get; }

    /// <summary>Gets the index of the first column of the cell.</summary>
    public int ColumnIndex { get; }

    /// <summary>Gets the number of columns of the cell.</summary>
    public int ColumnSpan { get; }

    /// <summary>Gets the number of rows of the cell.</summary>
    public int RowSpan { get; }

    /// <summary>Gets the text of the cell: the text of each of its lines between its <c>|</c>, without the whitespace at the end.</summary>
    public string Text { get; }

    /// <summary>Gets the content of the cell: its text, parsed with the options of the table, whose reference links can use the definitions of the tree of the table.</summary>
    public MarkdownSyntaxTree Content => _content ??= MarkdownSyntaxTree.ParseCell(Text, _table, _isClosed, _depth);

    /// <summary>
    /// Gets the position in the text of the tree of the table of a position in <see cref="Text"/>, the text of
    /// <see cref="Content"/>. The line ending that separates two lines of the text is the end of the first one.
    /// </summary>
    /// <param name="position">A position in <see cref="Text"/>, from 0 to its length.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is negative or greater than the length of <see cref="Text"/>.</exception>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs" ref:region="MarkdownGridTableCell_MapSpan" ref:dedent="true">
    /// var options = new MarkdownParseOptions { Extensions = MarkdownExtensions.GridTables };
    /// var text = "Intro\n\n+---------------+\n| See [a](/u)   |\n| and [b](/v)   |\n+---------------+\n";
    /// var tree = MarkdownSyntaxTree.ParseText(text, options);
    /// var cell = tree.GetRoot().Blocks.OfType&lt;MarkdownGridTableSyntax&gt;().Single().Rows[0].Cells[0];
    ///
    /// // The nodes of the content of the cell are not in the tree, and their spans are in the text of the cell
    /// Assert.Empty(tree.GetRoot().DescendantNodes().OfType&lt;MarkdownLinkSyntax&gt;());
    /// var link = cell.Content.GetRoot().DescendantNodes().OfType&lt;MarkdownLinkSyntax&gt;().Last();
    /// Assert.Equal("[b](/v)", cell.Text.Substring(link.Span.Start, link.Span.Length));
    ///
    /// // MapSpan gives the span of the link in the text of the tree
    /// var span = cell.MapSpan(link.Span);
    /// Assert.Equal("[b](/v)", text.Substring(span.Start, span.Length));
    /// </code>
    /// </example>
    public int MapPosition(int position)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(position, Text.Length);

        // The lines of the text are separated by \n
        var lineStart = 0;
        for (var i = 0; i < _segments.Length; i++)
        {
            var segment = _segments[i];
            if (position <= lineStart + segment.Length)
            {
                return segment.Start + position - lineStart;
            }

            lineStart += segment.Length + 1;
        }

        // A cell without lines has an empty text
        return _segments is [.., var last] ? last.End : 0;
    }

    /// <summary>
    /// Gets the span in the text of the tree of the table of a span of <see cref="Text"/>, the text of <see cref="Content"/>,
    /// such as the span of a node of <see cref="Content"/>. A span on several lines covers the borders of the cells between
    /// them, and the cells next to the cell on these lines.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The span ends after <see cref="Text"/>.</exception>
    public TextSpan MapSpan(TextSpan span)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(span.End, Text.Length, nameof(span));

        var start = MapPosition(span.Start);
        return span.IsEmpty ? new TextSpan(start, 0) : TextSpan.FromBounds(start, MapPosition(span.End));
    }
}
