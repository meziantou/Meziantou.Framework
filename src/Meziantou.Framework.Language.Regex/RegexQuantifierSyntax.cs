using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Regex;

/// <summary>The base of the operator part of a quantified term.</summary>
public abstract class RegexQuantifierSyntax : RegexSyntaxNode
{
    private protected RegexQuantifierSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the <c>?</c> or <c>+</c> that follows the operator. It is absent when the quantifier is greedy.</summary>
    public abstract SyntaxToken ModifierToken { get; }

    /// <summary>Gets how the quantifier backtracks.</summary>
    public RegexQuantifierMode Mode => ModifierToken.Text switch
    {
        "?" => RegexQuantifierMode.Lazy,
        "+" => RegexQuantifierMode.Possessive,
        _ => RegexQuantifierMode.Greedy,
    };

    /// <summary>Gets the smallest number of repetitions the quantifier accepts.</summary>
    public abstract int MinCount { get; }

    /// <summary>Gets the largest number of repetitions the quantifier accepts, or <see langword="null"/> when it is unbounded.</summary>
    public abstract int? MaxCount { get; }
}
