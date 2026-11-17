using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Markdown;

/// <summary>A paragraph: its inlines.</summary>
public sealed class MarkdownParagraphSyntax : MarkdownBlockSyntax
{
    internal MarkdownParagraphSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the inlines of the paragraph: the nodes of the constructs, and the tokens of the text between them.</summary>
    public ChildSyntaxList Inlines => ChildNodesAndTokens();

    public override string GetPlainText() => GetPlainText(ChildNodesAndTokens());

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownParagraph(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownParagraph(this);
    }
}
