using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Markdown;

/// <summary>A term of a definition: a line of inlines.</summary>
public sealed class MarkdownDefinitionTermSyntax : MarkdownBlockSyntax
{
    internal MarkdownDefinitionTermSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the inlines of the term.</summary>
    public ChildSyntaxList Inlines => ChildNodesAndTokens();

    public override string GetPlainText() => GetPlainText(ChildNodesAndTokens());

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownDefinitionTerm(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownDefinitionTerm(this);
    }
}
