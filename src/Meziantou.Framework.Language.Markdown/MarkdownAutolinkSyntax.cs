using Meziantou.Framework.Language.InternalSyntax;
using System.Text;

namespace Meziantou.Framework.Language.Markdown;

/// <summary>An autolink: a URL or an email address between <c>&lt;</c> and <c>&gt;</c>, or, in GitHub Flavored Markdown, in the text.</summary>
public sealed class MarkdownAutolinkSyntax : MarkdownInlineSyntax
{
    internal MarkdownAutolinkSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets a value indicating whether the link is an extended autolink of GitHub Flavored Markdown, which is not between angle brackets.</summary>
    public bool IsExtended => Kind() == SyntaxKind.ExtendedAutolink;

    /// <summary>Gets a value indicating whether the link is an email address.</summary>
    public bool IsEmail
    {
        get
        {
            if (!IsExtended)
            {
                var text = Text;
                return text.Contains('@', StringComparison.Ordinal) && !text.Contains(':', StringComparison.Ordinal);
            }

            return ChildTokens().First().RawKind != (int)SyntaxKind.ExtendedAutolinkToken;
        }
    }

    /// <summary>Gets the text of the link, as it is shown.</summary>
    public string Text
    {
        get
        {
            if (!IsExtended)
            {
                return GetTokenText(ChildTokens().First())[1..^1];
            }

            var builder = new StringBuilder();
            AppendPlainText(ChildNodesAndTokens(), builder);
            return builder.ToString();
        }
    }

    /// <summary>Gets the destination: <c>mailto:</c> before an email address, and <c>http://</c> before a <c>www.</c> address.</summary>
    public string Destination
    {
        get
        {
            var text = Text;
            if (IsEmail)
            {
                return "mailto:" + text;
            }

            return IsExtended && text.StartsWith("www.", StringComparison.Ordinal) ? "http://" + text : text;
        }
    }

    internal override void AppendPlainText(StringBuilder builder) => builder.Append(Text);

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownAutolink(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownAutolink(this);
    }
}
