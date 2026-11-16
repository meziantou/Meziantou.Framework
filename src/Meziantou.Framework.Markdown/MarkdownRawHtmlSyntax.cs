using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Markdown;

/// <summary>An HTML tag, comment, declaration or processing instruction in the text.</summary>
public sealed class MarkdownRawHtmlSyntax : MarkdownInlineSyntax
{
    internal MarkdownRawHtmlSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the HTML, with <c>\n</c> for its line breaks.</summary>
    public string Html
    {
        get
        {
            // The HTML is most often one token: its text is the HTML
            string? html = null;
            StringBuilder? builder = null;
            foreach (var child in ChildNodesAndTokens())
            {
                if (!child.AsToken(out var token))
                {
                    continue;
                }

                var text = token.RawKind == (int)SyntaxKind.LineEndingToken ? "\n" : GetTokenText(token);
                if (html is null)
                {
                    html = text;
                }
                else
                {
                    builder ??= new StringBuilder(html);
                    builder.Append(text);
                }
            }

            return builder?.ToString() ?? html ?? "";
        }
    }

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
