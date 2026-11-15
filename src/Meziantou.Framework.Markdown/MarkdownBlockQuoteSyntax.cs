using Meziantou.Framework.Language.InternalSyntax;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>A block quote: blocks after <c>&gt;</c>.</summary>
public sealed class MarkdownBlockQuoteSyntax : MarkdownBlockSyntax
{
    internal MarkdownBlockQuoteSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    private IReadOnlyList<MarkdownBlockSyntax>? _blocks;

    /// <summary>Gets the <c>&gt;</c> of the first line. The markers of the next lines are trivia.</summary>
    public SyntaxToken MarkerToken => GetToken(SyntaxKind.BlockQuoteMarkerToken);

    /// <summary>Gets the blocks of the quote.</summary>
    public IReadOnlyList<MarkdownBlockSyntax> Blocks => GetNodes(ref _blocks);

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownBlockQuote(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownBlockQuote(this);
    }
}
