using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Regex;

/// <summary>The base of every parenthesized construct.</summary>
public abstract class RegexGroupSyntax : RegexAtomSyntax
{
    private protected RegexGroupSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the <c>(</c> that opens the group.</summary>
    public abstract SyntaxToken OpenParenToken { get; }

    /// <summary>Gets the <c>)</c> that closes the group. It is missing when the pattern ends before the group is closed.</summary>
    public abstract SyntaxToken CloseParenToken { get; }

    /// <summary>Gets the options in effect inside the group, after any inline options in its header were applied.</summary>
    public RegexPatternOptions InnerOptions => ((Syntax.InternalSyntax.RegexGroupSyntax)Green).InnerOptions;
}
