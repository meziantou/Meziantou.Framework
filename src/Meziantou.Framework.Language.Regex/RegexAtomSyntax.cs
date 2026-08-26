namespace Meziantou.Framework.Language.Regex;

/// <summary>Base type for a single unquantified unit of a pattern, such as a literal, a class, or a group.</summary>
public abstract class RegexAtomSyntax : RegexTermSyntax
{
    private protected RegexAtomSyntax(RegexSyntaxKind kind, string fullText, int fullStart = 0, IReadOnlyList<RegexSyntaxToken>? tokens = null)
        : base(kind, fullText, fullStart, tokens)
    {
    }

    private protected RegexAtomSyntax(RegexSyntaxKind kind, IReadOnlyList<RegexSyntaxToken?>? tokens, params ReadOnlySpan<RegexSyntaxNodeOrToken> parts)
        : base(kind, tokens, parts)
    {
    }
}
