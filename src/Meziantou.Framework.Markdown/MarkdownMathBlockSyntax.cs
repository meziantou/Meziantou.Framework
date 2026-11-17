using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Markdown.Parsing;
using System.Text;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>A math block: lines of math between <c>$$</c> lines.</summary>
public sealed class MarkdownMathBlockSyntax : MarkdownBlockSyntax
{
    internal MarkdownMathBlockSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the opening <c>$$</c>.</summary>
    public SyntaxToken OpeningFenceToken => GetToken(SyntaxKind.MathFenceToken);

    /// <summary>Gets the closing <c>$$</c>, or the default token when the block runs to the end of its container.</summary>
    public SyntaxToken ClosingFenceToken
    {
        get
        {
            var fences = GetTokens(SyntaxKind.MathFenceToken);
            return fences.Count == 2 ? fences[1] : default;
        }
    }

    /// <summary>Gets the tokens of the lines of math, with their line endings.</summary>
    public IReadOnlyList<SyntaxToken> LineTokens => GetTokens(SyntaxKind.MathLineToken);

    /// <summary>Gets the math: its lines without the indentation of the block and the markers of its containers, each one ended by <c>\n</c>.</summary>
    public string Math
    {
        get
        {
            var builder = new StringBuilder();
            foreach (var child in ChildNodesAndTokens())
            {
                if (!child.AsToken(out var token))
                {
                    continue;
                }

                if (token.RawKind == (int)SyntaxKind.MathLineToken)
                {
                    if (token.Value is int virtualSpaces)
                    {
                        builder.Append(' ', virtualSpaces);
                    }

                    builder.Append(MarkdownText.TrimLineEnding(token.Text)).Append('\n');
                }
            }

            return builder.ToString();
        }
    }

    public override string GetPlainText() => Math;

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownMathBlock(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownMathBlock(this);
    }
}
