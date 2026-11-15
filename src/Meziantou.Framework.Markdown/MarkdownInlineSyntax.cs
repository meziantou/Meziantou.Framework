using System.Text;
using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Markdown;

/// <summary>An inline that has a structure: an emphasis, a code span, a link, an autolink, raw HTML...</summary>
/// <remarks>The text between the inlines is made of tokens, such as <see cref="SyntaxKind.TextToken"/>.</remarks>
public abstract class MarkdownInlineSyntax : MarkdownSyntaxNode
{
    private protected MarkdownInlineSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public override string GetPlainText()
    {
        var builder = new StringBuilder();
        AppendPlainText(builder);
        return builder.ToString();
    }

    internal abstract void AppendPlainText(StringBuilder builder);
}
