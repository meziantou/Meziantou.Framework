using Meziantou.Framework.Language.InternalSyntax;
using System.Text;

namespace Meziantou.Framework.Markdown;

/// <summary>A code span: code between backticks.</summary>
public sealed class MarkdownCodeSpanSyntax : MarkdownInlineSyntax
{
    internal MarkdownCodeSpanSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the code: its line endings are spaces, and a space on each side is removed when the code is not only spaces.</summary>
    public string Code
    {
        get
        {
            var content = new StringBuilder();
            var tokens = ChildTokens().ToList();
            for (var i = 1; i < tokens.Count - 1; i++)
            {
                content.Append(tokens[i].RawKind == (int)SyntaxKind.LineEndingToken ? " " : GetTokenText(tokens[i]));
            }

            var text = content.ToString();
            if (text.Length >= 2 && text[0] == ' ' && text[^1] == ' ' && text.AsSpan().ContainsAnyExcept(' '))
            {
                text = text[1..^1];
            }

            return text;
        }
    }

    internal override void AppendPlainText(StringBuilder builder) => builder.Append(Code);

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownCodeSpan(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownCodeSpan(this);
    }
}
