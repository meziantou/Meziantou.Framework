namespace Meziantou.Framework.Language.Regex;

/// <summary>Base type for the operator part of a quantified term.</summary>
public abstract class RegexQuantifierSyntax : RegexSyntaxNode
{
    private protected RegexQuantifierSyntax(RegexSyntaxKind kind, IReadOnlyList<RegexSyntaxToken?>? tokens, params ReadOnlySpan<RegexSyntaxNodeOrToken> parts)
        : base(kind, tokens, parts)
    {
    }

    /// <summary>The <c>?</c> or <c>+</c> that follows the operator, or <see langword="null"/> when the quantifier is greedy.</summary>
    public abstract RegexSyntaxToken? ModifierToken { get; }

    /// <summary>How the quantifier backtracks.</summary>
    public RegexQuantifierMode Mode => ModifierToken?.Text switch
    {
        "?" => RegexQuantifierMode.Lazy,
        "+" => RegexQuantifierMode.Possessive,
        _ => RegexQuantifierMode.Greedy,
    };

    /// <summary>The smallest number of repetitions the quantifier accepts.</summary>
    public abstract int MinCount { get; }

    /// <summary>The largest number of repetitions the quantifier accepts, or <see langword="null"/> when it is unbounded.</summary>
    public abstract int? MaxCount { get; }
}
