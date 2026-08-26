namespace Meziantou.Framework.Language.Regex;

/// <summary>Base type for every parenthesized construct.</summary>
public abstract class RegexGroupSyntax : RegexAtomSyntax
{
    private protected RegexGroupSyntax(RegexSyntaxKind kind, IReadOnlyList<RegexSyntaxToken?>? tokens, params ReadOnlySpan<RegexSyntaxNodeOrToken> parts)
        : base(kind, tokens, parts)
    {
    }

    /// <summary>The <c>(</c> that opens the group.</summary>
    public abstract RegexSyntaxToken OpenParenToken { get; }

    /// <summary>
    /// The <c>)</c> that closes the group. It is missing when the pattern ends before the group is closed.
    /// </summary>
    public abstract RegexSyntaxToken CloseParenToken { get; }

    /// <summary>The options in effect inside the group, after any inline options in its header were applied.</summary>
    public RegexPatternOptions InnerOptions { get; internal set; }
}
