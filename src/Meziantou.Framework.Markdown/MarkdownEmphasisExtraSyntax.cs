using Meziantou.Framework.Language.InternalSyntax;
using System.Text;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>An extra emphasis: subscript (<c>~text~</c>), superscript (<c>^text^</c>), inserted (<c>++text++</c>), marked text (<c>==text==</c>) or an inline custom container (<c>::text::</c>).</summary>
public sealed class MarkdownEmphasisExtraSyntax : MarkdownInlineSyntax
{
    internal MarkdownEmphasisExtraSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the style of the text.</summary>
    public MarkdownEmphasisExtraStyle Style => Kind() switch
    {
        SyntaxKind.Subscript => MarkdownEmphasisExtraStyle.Subscript,
        SyntaxKind.Superscript => MarkdownEmphasisExtraStyle.Superscript,
        SyntaxKind.Inserted => MarkdownEmphasisExtraStyle.Inserted,
        SyntaxKind.CustomContainerInline => MarkdownEmphasisExtraStyle.CustomContainer,
        SyntaxKind.Citation => MarkdownEmphasisExtraStyle.Citation,
        _ => MarkdownEmphasisExtraStyle.Marked,
    };

    /// <summary>Gets the delimiters before the content.</summary>
    public SyntaxToken OpeningDelimiterToken => GetFirstChildToken();

    /// <summary>Gets the delimiters after the content.</summary>
    public SyntaxToken ClosingDelimiterToken => GetLastChildToken();

    /// <summary>Gets the inlines between the delimiters.</summary>
    public IEnumerable<SyntaxNodeOrToken> Inlines
    {
        get
        {
            var children = ChildNodesAndTokens();
            return children.Skip(1).Take(children.Count - 2);
        }
    }

    internal override void AppendPlainText(StringBuilder builder) => AppendPlainText(Inlines, builder);

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownEmphasisExtra(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownEmphasisExtra(this);
    }
}
