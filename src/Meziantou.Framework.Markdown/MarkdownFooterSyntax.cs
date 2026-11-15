using Meziantou.Framework.Language.InternalSyntax;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>A footer: lines that start with <c>^^</c>, as a block quote does with <c>&gt;</c>.</summary>
public sealed class MarkdownFooterSyntax : MarkdownBlockSyntax
{
    internal MarkdownFooterSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    private IReadOnlyList<MarkdownBlockSyntax>? _blocks;

    /// <summary>Gets the <c>^^</c> of the first line. The markers of the next lines are trivia.</summary>
    public SyntaxToken MarkerToken => GetToken(SyntaxKind.FooterMarkerToken);

    /// <summary>Gets the blocks of the footer, whose paragraphs are rendered without <c>&lt;p&gt;</c>.</summary>
    public IReadOnlyList<MarkdownBlockSyntax> Blocks => GetNodes(ref _blocks);

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownFooter(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownFooter(this);
    }
}
