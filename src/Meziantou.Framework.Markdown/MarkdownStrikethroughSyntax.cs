using Meziantou.Framework.Language.InternalSyntax;
using System.Text;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>A strikethrough of GitHub Flavored Markdown: text between <c>~</c> or <c>~~</c>.</summary>
public sealed class MarkdownStrikethroughSyntax : MarkdownInlineSyntax
{
    internal MarkdownStrikethroughSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the tildes before the content.</summary>
    public SyntaxToken OpeningDelimiterToken => GetFirstChildToken();

    /// <summary>Gets the tildes after the content.</summary>
    public SyntaxToken ClosingDelimiterToken => GetLastChildToken();

    /// <summary>Gets the inlines between the tildes.</summary>
    public IEnumerable<SyntaxNodeOrToken> Inlines => ChildNodesAndTokens().Where(child => !child.IsToken || child.AsToken().RawKind != (int)SyntaxKind.StrikethroughDelimiterToken);

    internal override void AppendPlainText(StringBuilder builder) => AppendPlainText(Inlines, builder);

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownStrikethrough(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownStrikethrough(this);
    }
}
