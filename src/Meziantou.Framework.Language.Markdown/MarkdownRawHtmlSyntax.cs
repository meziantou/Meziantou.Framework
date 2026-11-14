using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Markdown;

/// <summary>An HTML tag, comment, declaration or processing instruction in the text.</summary>
public sealed class MarkdownRawHtmlSyntax : MarkdownInlineSyntax
{
    internal MarkdownRawHtmlSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the HTML, with <c>\n</c> for its line breaks.</summary>
    public string Html => string.Concat(ChildTokens().Select(token => token.RawKind == (int)SyntaxKind.LineEndingToken ? "\n" : GetTokenText(token)));

    internal override void AppendPlainText(StringBuilder builder)
    {
    }

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownRawHtml(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownRawHtml(this);
    }
}
