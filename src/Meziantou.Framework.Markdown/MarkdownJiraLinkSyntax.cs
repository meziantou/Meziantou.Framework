using System.Text;
using Meziantou.Framework.Language.InternalSyntax;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>A reference to a Jira issue, such as <c>PROJ-123</c>, which is a link to it.</summary>
public sealed class MarkdownJiraLinkSyntax : MarkdownInlineSyntax
{
    internal MarkdownJiraLinkSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the reference, such as <c>PROJ-123</c>.</summary>
    public SyntaxToken ReferenceToken => GetFirstChildToken();

    /// <summary>Gets the key of the project: the letters and the digits before the hyphen.</summary>
    public string ProjectKey => ReferenceToken.Text[..ReferenceToken.Text.IndexOf('-', StringComparison.Ordinal)];

    /// <summary>Gets the number of the issue: the digits after the hyphen.</summary>
    public string IssueNumber => ReferenceToken.Text[(ReferenceToken.Text.IndexOf('-', StringComparison.Ordinal) + 1)..];

    /// <summary>Gets the destination of the link, the URL of the issue, from <see cref="MarkdownParseOptions.JiraLinks"/>.</summary>
    public string Destination => ((SyntaxTree as MarkdownSyntaxTree)?.Options ?? MarkdownParseOptions.Default).JiraLinks.GetDestination(ReferenceToken.Text);

    internal override void AppendPlainText(StringBuilder builder) => builder.Append(ReferenceToken.Text);

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownJiraLink(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownJiraLink(this);
    }
}
