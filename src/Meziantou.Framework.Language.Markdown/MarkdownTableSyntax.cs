using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Markdown;

/// <summary>A table of GitHub Flavored Markdown: a header row, a delimiter row, and the other rows.</summary>
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

    /// <summary>Gets the alignment of each column: the table has as many columns as the delimiter row has cells.</summary>
    public IReadOnlyList<MarkdownTableAlignment> Alignments => [.. DelimiterRow.DelimiterTokens.Select(token => (token.Text[0] == ':', token.Text[^1] == ':') switch
    {
        (true, true) => MarkdownTableAlignment.Center,
        (true, false) => MarkdownTableAlignment.Left,
        (false, true) => MarkdownTableAlignment.Right,
        _ => MarkdownTableAlignment.None,
    })];

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
