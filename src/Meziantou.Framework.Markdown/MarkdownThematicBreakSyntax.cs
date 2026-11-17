using Meziantou.Framework.Language.InternalSyntax;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>A thematic break: <c>---</c>, <c>***</c> or <c>___</c>.</summary>
public sealed class MarkdownThematicBreakSyntax : MarkdownBlockSyntax
{
    internal MarkdownThematicBreakSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the characters of the break.</summary>
    public SyntaxToken BreakToken => GetToken(SyntaxKind.ThematicBreakToken);

    public override string GetPlainText() => "";

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownThematicBreak(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownThematicBreak(this);
    }
}
