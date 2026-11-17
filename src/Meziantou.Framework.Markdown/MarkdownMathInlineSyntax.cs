using Meziantou.Framework.Language.InternalSyntax;
using System.Text;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>Inline math: text between <c>$</c> or <c>$$</c>.</summary>
public sealed class MarkdownMathInlineSyntax : MarkdownInlineSyntax
{
    internal MarkdownMathInlineSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the <c>$</c> or <c>$$</c> that open the math.</summary>
    public SyntaxToken OpeningDelimiterToken => GetFirstChildToken();

    /// <summary>Gets the <c>$</c> that close the math.</summary>
    public SyntaxToken ClosingDelimiterToken => GetLastChildToken();

    /// <summary>Gets the math, without the whitespace around it. A backslash is text of the math, as in LaTeX.</summary>
    public string Math => GetToken(SyntaxKind.MathTextToken) is { RawKind: not 0 } token ? token.Text : "";

    internal override void AppendPlainText(StringBuilder builder) => builder.Append(Math);

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownMathInline(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownMathInline(this);
    }
}
