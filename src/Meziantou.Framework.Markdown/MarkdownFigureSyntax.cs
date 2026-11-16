using Meziantou.Framework.Language.InternalSyntax;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>A figure: blocks between a fence of three carets or more, <c>^^^</c>, and a fence as long, with the captions after the fences.</summary>
public sealed class MarkdownFigureSyntax : MarkdownBlockSyntax
{
    internal MarkdownFigureSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    private IReadOnlyList<MarkdownBlockSyntax>? _blocks;

    /// <summary>Gets the carets that open the figure.</summary>
    public SyntaxToken OpeningFenceToken => GetFirstChildToken();

    /// <summary>Gets the carets that close the figure, or the default token when the figure is not closed.</summary>
    public SyntaxToken ClosingFenceToken => ChildTokens().Skip(1).LastOrDefault(token => token.RawKind == (int)SyntaxKind.FigureFenceToken);

    /// <summary>Gets the blocks of the figure, with its captions, which are the text after its fences.</summary>
    public IReadOnlyList<MarkdownBlockSyntax> Blocks => GetNodes(ref _blocks);

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownFigure(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownFigure(this);
    }
}
