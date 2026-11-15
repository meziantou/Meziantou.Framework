using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Markdown;

/// <summary>A cell of a row of a table: its inlines.</summary>
public sealed class MarkdownTableCellSyntax : MarkdownSyntaxNode
{
    internal MarkdownTableCellSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the inlines of the cell. In a cell of a table of GFM, <c>\|</c> is a pipe, even in a code span.</summary>
    public ChildSyntaxList Inlines => ChildNodesAndTokens();

    public override string GetPlainText() => GetPlainText(ChildNodesAndTokens());

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownTableCell(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownTableCell(this);
    }
}
