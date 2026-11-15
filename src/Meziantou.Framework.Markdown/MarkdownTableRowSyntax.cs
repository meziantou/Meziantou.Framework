using Meziantou.Framework.Language.InternalSyntax;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>A row of a table: its cells, and the pipes between them.</summary>
public sealed class MarkdownTableRowSyntax : MarkdownSyntaxNode
{
    internal MarkdownTableRowSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    private IReadOnlyList<MarkdownTableCellSyntax>? _cells;

    /// <summary>Gets a value indicating whether this is the header row of its table.</summary>
    public bool IsHeader => Kind() == SyntaxKind.TableHeaderRow;

    /// <summary>Gets a value indicating whether this is the delimiter row of its table, whose cells are <see cref="DelimiterTokens"/>.</summary>
    public bool IsDelimiter => Kind() == SyntaxKind.TableDelimiterRow;

    /// <summary>Gets the cells of the row. A delimiter row has none: its cells are tokens.</summary>
    /// <remarks>A row can have more cells than its table has columns, which are not rendered, or fewer, which are rendered empty.</remarks>
    public IReadOnlyList<MarkdownTableCellSyntax> Cells => GetNodes(ref _cells);

    /// <summary>Gets the cells of a delimiter row, such as <c>:--:</c>.</summary>
    public IReadOnlyList<SyntaxToken> DelimiterTokens => GetTokens(SyntaxKind.TableDelimiterToken);

    /// <summary>Gets the pipes of the row.</summary>
    public IReadOnlyList<SyntaxToken> PipeTokens => GetTokens(SyntaxKind.PipeToken);

    public override string GetPlainText() => string.Join('\t', Cells.Select(cell => cell.GetPlainText()));

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownTableRow(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownTableRow(this);
    }
}
