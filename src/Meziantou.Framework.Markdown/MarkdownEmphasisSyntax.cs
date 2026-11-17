using Meziantou.Framework.Language.InternalSyntax;
using System.Text;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>An emphasis (<c>*text*</c>) or a strong emphasis (<c>**text**</c>).</summary>
public sealed class MarkdownEmphasisSyntax : MarkdownInlineSyntax
{
    internal MarkdownEmphasisSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets a value indicating whether the emphasis is strong: two delimiters on each side.</summary>
    public bool IsStrong => Kind() == SyntaxKind.StrongEmphasis;

    /// <summary>Gets the delimiters before the content.</summary>
    public SyntaxToken OpeningDelimiterToken => GetFirstChildToken();

    /// <summary>Gets the delimiters after the content.</summary>
    public SyntaxToken ClosingDelimiterToken => GetLastChildToken();

    /// <summary>Gets the inlines between the delimiters.</summary>
    public IEnumerable<SyntaxNodeOrToken> Inlines => ChildNodesAndTokens().Where(child => !child.IsToken || child.AsToken().RawKind != (int)SyntaxKind.EmphasisDelimiterToken);

    internal override void AppendPlainText(StringBuilder builder) => AppendPlainText(Inlines, builder);

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownEmphasis(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownEmphasis(this);
    }
}
