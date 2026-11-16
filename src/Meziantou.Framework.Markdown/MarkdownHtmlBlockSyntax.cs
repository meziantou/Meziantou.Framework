using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Markdown.Parsing;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>A block of raw HTML.</summary>
public sealed class MarkdownHtmlBlockSyntax : MarkdownBlockSyntax
{
    internal MarkdownHtmlBlockSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the tokens of the lines of the block, with their line endings.</summary>
    public IReadOnlyList<SyntaxToken> LineTokens => GetTokens(SyntaxKind.HtmlLineToken);

    /// <summary>Gets the HTML: the lines of the block joined by <c>\n</c>, without the line ending of the last one.</summary>
    public string Html
    {
        get
        {
            var builder = new StringBuilder();
            AppendHtml(builder);
            return builder.ToString();
        }
    }

    /// <summary>Appends the HTML, as <see cref="Html"/> has it.</summary>
    internal void AppendHtml(StringBuilder builder)
    {
        var first = true;
        foreach (var child in ChildNodesAndTokens())
        {
            if (child.AsToken(out var token) && token.RawKind == (int)SyntaxKind.HtmlLineToken)
            {
                if (!first)
                {
                    builder.Append('\n');
                }

                builder.Append(MarkdownText.TrimLineEnding(token.Text));
                first = false;
            }
        }
    }

    public override string GetPlainText() => "";

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownHtmlBlock(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownHtmlBlock(this);
    }
}
