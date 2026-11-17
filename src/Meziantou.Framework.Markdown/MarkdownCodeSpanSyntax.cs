using Meziantou.Framework.Language.InternalSyntax;
using System.Text;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

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
            // The tokens between the backticks: each one is written when the next one shows it is not the closing backticks
            StringBuilder? content = null;
            string? text = null;
            var count = 0;
            SyntaxToken previous = default;
            foreach (var child in ChildNodesAndTokens())
            {
                if (!child.AsToken(out var token))
                {
                    continue;
                }

                if (count++ >= 2)
                {
                    var previousText = previous.RawKind == (int)SyntaxKind.LineEndingToken ? " " : GetTokenText(previous);
                    if (text is null)
                    {
                        text = previousText;
                    }
                    else
                    {
                        content ??= new StringBuilder(text);
                        content.Append(previousText);
                    }
                }

                previous = token;
            }

            text = content?.ToString() ?? text ?? "";
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
