namespace Meziantou.Framework.Language.Regex;

/// <summary>Base type for anything that can appear as one element of a <see cref="RegexSequenceSyntax"/>.</summary>
public abstract class RegexTermSyntax : RegexSyntaxNode
{
    private protected RegexTermSyntax(RegexSyntaxKind kind, string fullText, int fullStart = 0, IReadOnlyList<RegexSyntaxToken>? tokens = null)
        : base(kind, fullText, fullStart, tokens)
    {
    }

    private protected RegexTermSyntax(RegexSyntaxKind kind, IReadOnlyList<RegexSyntaxToken?>? tokens, params ReadOnlySpan<RegexSyntaxNodeOrToken> parts)
        : base(kind, tokens, parts)
    {
    }
}
