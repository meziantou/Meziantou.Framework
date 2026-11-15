using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Markdown;

/// <summary>The caption of a figure: the text after one of its fences.</summary>
public sealed class MarkdownFigureCaptionSyntax : MarkdownBlockSyntax
{
    internal MarkdownFigureCaptionSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the inlines of the caption.</summary>
    public ChildSyntaxList Inlines => ChildNodesAndTokens();

    public override string GetPlainText() => GetPlainText(ChildNodesAndTokens());

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownFigureCaption(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownFigureCaption(this);
    }
}
