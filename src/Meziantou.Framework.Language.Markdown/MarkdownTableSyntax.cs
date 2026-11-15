using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Markdown.Parsing;

namespace Meziantou.Framework.Language.Markdown;

/// <summary>A table of GitHub Flavored Markdown, or a pipe table of Markdig: a header row, a delimiter row, and the other rows.</summary>
public sealed class MarkdownTableSyntax : MarkdownBlockSyntax
{
    internal MarkdownTableSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    private IReadOnlyList<MarkdownTableRowSyntax>? _allRows;

    /// <summary>Gets the header row.</summary>
    public MarkdownTableRowSyntax HeaderRow => AllRows[0];

    /// <summary>Gets the delimiter row, whose cells give the alignments of the columns.</summary>
    public MarkdownTableRowSyntax DelimiterRow => AllRows[1];

    /// <summary>Gets the rows after the delimiter row.</summary>
    public IReadOnlyList<MarkdownTableRowSyntax> Rows => [.. AllRows.Skip(2)];

    /// <summary>Gets a value indicating whether this is a pipe table of Markdig, read with <see cref="MarkdownExtensions.PipeTables"/>, and not a table of GitHub Flavored Markdown.</summary>
    public bool IsPipeTable => Kind() == SyntaxKind.PipeTable;

    /// <summary>Gets the alignment of each column of the delimiter row.</summary>
    /// <remarks>
    /// A table of GitHub Flavored Markdown has as many columns as its delimiter row has cells. A pipe table has as many columns as
    /// its longest row has cells, and the columns after the last cell of the delimiter row have its alignment. A cell of the
    /// delimiter row of a pipe table can be empty, which is a column without alignment.
    /// </remarks>
    public IReadOnlyList<MarkdownTableAlignment> Alignments => IsPipeTable ? GetPipeTableAlignments() : [.. DelimiterRow.DelimiterTokens.Select(token => (token.Text[0] == ':', token.Text[^1] == ':') switch
    {
        (true, true) => MarkdownTableAlignment.Center,
        (true, false) => MarkdownTableAlignment.Left,
        (false, true) => MarkdownTableAlignment.Right,
        _ => MarkdownTableAlignment.None,
    })];

    private MarkdownTableAlignment[] GetPipeTableAlignments()
    {
        // The generic attributes of the delimiter row are not part of its cells
        var row = DelimiterRow;
        var characters = row.ToString().ToCharArray();
        foreach (var attributes in row.ChildNodes())
        {
            characters.AsSpan(attributes.Span.Start - row.Span.Start, attributes.Span.Length).Fill(' ');
        }

        var text = new string(characters);
        var pipes = new List<int>();
        foreach (var token in row.PipeTokens)
        {
            pipes.Add(token.Span.Start - row.Span.Start);
        }

        return PipeTableScanner.ReadColumns(text, 0, text.Length, pipes, hasLineEnd: true) is { } columns ? [.. columns.Select(column => column.Alignment)] : [];
    }

    private IReadOnlyList<MarkdownTableRowSyntax> AllRows => GetNodes(ref _allRows);

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownTable(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownTable(this);
    }
}
