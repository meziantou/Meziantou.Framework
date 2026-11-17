namespace Meziantou.Framework.Markdown;

/// <summary>A cell of a grid table: its text is a document of its own.</summary>
public sealed class MarkdownGridTableCell
{
    private readonly MarkdownSyntaxTree? _table;
    private MarkdownSyntaxTree? _content;

    private readonly bool _isClosed;

    // The blocks that the content is in: the table, and its containers in its tree and the trees around it
    private readonly int _depth;

    internal MarkdownGridTableCell(int columnIndex, int columnSpan, int rowSpan, string text, MarkdownSyntaxTree? table, bool isClosed, int lineIndex, int depth)
    {
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
}
