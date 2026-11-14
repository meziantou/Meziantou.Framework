using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Markdown;

/// <summary>An ATX heading (<c># Title</c>) or a setext heading (a paragraph underlined by <c>=</c> or <c>-</c>).</summary>
public sealed class MarkdownHeadingSyntax : MarkdownBlockSyntax
{
    internal MarkdownHeadingSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets a value indicating whether the heading is underlined rather than opened by <c>#</c>.</summary>
    public bool IsSetext => Kind() == SyntaxKind.SetextHeading;

    /// <summary>Gets the level of the heading, from 1 to 6: a setext heading is of level 1 or 2.</summary>
    public int Level
    {
        get
        {
            if (IsSetext)
            {
                return UnderlineToken.Text[0] == '=' ? 1 : 2;
            }

            return MarkerToken.Text.Length;
        }
    }

    /// <summary>Gets the <c>#</c> that open an ATX heading, or the default token for a setext heading.</summary>
    public SyntaxToken MarkerToken => GetToken(SyntaxKind.AtxHeadingMarkerToken);

    /// <summary>Gets the optional <c>#</c> that close an ATX heading, or the default token when there are none.</summary>
    public SyntaxToken ClosingSequenceToken => GetToken(SyntaxKind.AtxHeadingClosingSequenceToken);

    /// <summary>Gets the <c>=</c> or <c>-</c> that underline a setext heading, or the default token for an ATX heading.</summary>
    public SyntaxToken UnderlineToken => GetToken(SyntaxKind.SetextHeadingUnderlineToken);

    /// <summary>Gets the inlines of the heading, without its markers.</summary>
    public IEnumerable<SyntaxNodeOrToken> Inlines => ChildNodesAndTokens().Where(child => !child.IsToken || child.AsToken().RawKind is not ((int)SyntaxKind.AtxHeadingMarkerToken or (int)SyntaxKind.AtxHeadingClosingSequenceToken or (int)SyntaxKind.SetextHeadingUnderlineToken));

    public override string GetPlainText() => GetPlainText(Inlines);

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownHeading(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownHeading(this);
    }
}
