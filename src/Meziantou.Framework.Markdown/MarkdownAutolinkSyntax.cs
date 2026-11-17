using Meziantou.Framework.Language.InternalSyntax;
using System.Text;

namespace Meziantou.Framework.Markdown;

/// <summary>An autolink: a URL or an email address between <c>&lt;</c> and <c>&gt;</c>, or, in GitHub Flavored Markdown, in the text.</summary>
public sealed class MarkdownAutolinkSyntax : MarkdownInlineSyntax
{
    internal MarkdownAutolinkSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets a value indicating whether the link is an extended autolink of GitHub Flavored Markdown, which is not between angle brackets.</summary>
    public bool IsExtended => Kind() is SyntaxKind.ExtendedAutolink or SyntaxKind.MarkdigAutolink;

    /// <summary>Gets a value indicating whether the link is an autolink of Markdig, read with <see cref="MarkdownExtensions.MarkdigAutolinks"/>.</summary>
    public bool IsMarkdigAutolink => Kind() == SyntaxKind.MarkdigAutolink;

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

            if (IsMarkdigAutolink)
            {
                return GetFirstChildToken().Text.StartsWith("mailto:", StringComparison.Ordinal);
            }

            return GetFirstChildToken().RawKind != (int)SyntaxKind.ExtendedAutolinkToken;
        }
    }

    /// <summary>Gets the text of the link, as it is shown.</summary>
    public string Text
    {
        get
        {
            if (!IsExtended)
            {
                return GetTokenText(GetFirstChildToken())[1..^1];
            }

            // Markdig writes the email address of mailto: and the number of tel:
            if (IsMarkdigAutolink)
            {
                var url = GetTokenText(GetFirstChildToken());
                return url.StartsWith("mailto:", StringComparison.Ordinal) ? url[7..] : url.StartsWith("tel:", StringComparison.Ordinal) ? url[4..] : url;
            }

            var builder = new StringBuilder();
            AppendPlainText(ChildNodesAndTokens(), builder);
            return builder.ToString();
        }
    }

    /// <summary>Gets the destination: <c>mailto:</c> before an email address, and <c>http://</c> before a <c>www.</c> address (or <c>https://</c> for <see cref="MarkdownAutolinkOptions.UseHttpsForWwwLinks"/>).</summary>
    public string Destination
    {
        get
        {
            if (IsMarkdigAutolink)
            {
                var url = GetTokenText(GetFirstChildToken());
                var https = ((SyntaxTree as MarkdownSyntaxTree)?.Options ?? MarkdownParseOptions.Default).AutolinkOptions.UseHttpsForWwwLinks;
                return url.StartsWith("www.", StringComparison.Ordinal) ? (https ? "https://" : "http://") + url : url;
            }

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
