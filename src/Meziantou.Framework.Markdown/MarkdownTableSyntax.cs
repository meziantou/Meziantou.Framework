using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Markdown.Parsing;

namespace Meziantou.Framework.Markdown;

/// <summary>
/// A table of GitHub Flavored Markdown, or a pipe table of Markdig: a header row, a delimiter row, and the other rows. A pipe table
/// that <see cref="MarkdownPipeTableOptions.RequireHeaderSeparator"/> allows has only the other rows.
/// </summary>
public sealed class MarkdownTableSyntax : MarkdownBlockSyntax
{
    internal MarkdownTableSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    private IReadOnlyList<MarkdownTableRowSyntax>? _allRows;

    /// <summary>Gets the header row, or <see langword="null"/> when the table has no delimiter row.</summary>
    public MarkdownTableRowSyntax? HeaderRow => AllRows is [{ IsHeader: true } header, ..] ? header : null;

    /// <summary>Gets the delimiter row, whose cells give the alignments of the columns, or <see langword="null"/> when the table has none.</summary>
    public MarkdownTableRowSyntax? DelimiterRow => AllRows is [_, { IsDelimiter: true } delimiter, ..] ? delimiter : null;

    /// <summary>Gets the rows after the delimiter row, or all the rows when the table has no delimiter row.</summary>
    public IReadOnlyList<MarkdownTableRowSyntax> Rows => [.. AllRows.Where(row => !row.IsHeader && !row.IsDelimiter)];

    /// <summary>Gets a value indicating whether this is a pipe table of Markdig, read with <see cref="MarkdownExtensions.PipeTables"/>, and not a table of GitHub Flavored Markdown.</summary>
    public bool IsPipeTable => Kind() == SyntaxKind.PipeTable;

    /// <summary>Gets the alignment of each column of the delimiter row.</summary>
    /// <remarks>
    /// A table of GitHub Flavored Markdown has as many columns as its delimiter row has cells. A pipe table has as many columns as
    /// its longest row has cells, and the columns after the last cell of the delimiter row have its alignment. A cell of the
    /// delimiter row of a pipe table can be empty, which is a column without alignment.
    /// </remarks>
    public IReadOnlyList<MarkdownTableAlignment> Alignments => DelimiterRow is not { } delimiterRow ? [] : IsPipeTable ? [.. GetPipeTableColumns(delimiterRow).Select(column => column.Alignment)] : [.. delimiterRow.DelimiterTokens.Select(token => (token.Text[0] == ':', token.Text[^1] == ':') switch
    {
        (true, true) => MarkdownTableAlignment.Center,
        (true, false) => MarkdownTableAlignment.Left,
        (false, true) => MarkdownTableAlignment.Right,
        _ => MarkdownTableAlignment.None,
    })];

    /// <summary>Gets the alignment and the number of dashes of each column of the delimiter row of a pipe table.</summary>
    internal IReadOnlyList<(MarkdownTableAlignment Alignment, int DashCount)> GetPipeTableColumns() => DelimiterRow is { } row ? GetPipeTableColumns(row) : [];

    private static List<(MarkdownTableAlignment Alignment, int DashCount)> GetPipeTableColumns(MarkdownTableRowSyntax row)
    {
        // The generic attributes of the delimiter row, and the text before them in their cell, are not part of its cells
        var characters = row.ToString().ToCharArray();
        foreach (var attributes in row.ChildNodes())
        {
            PipeTableScanner.BlankAttributes(characters, 0, attributes.Span.Start - row.Span.Start, attributes.Span.End - row.Span.Start);
        }

        var text = new string(characters);
        var pipes = new List<int>();
        foreach (var token in row.PipeTokens)
        {
            pipes.Add(token.Span.Start - row.Span.Start);
        }

        return PipeTableScanner.ReadColumns(text, 0, text.Length, pipes, hasLineEnd: true) ?? [];
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
