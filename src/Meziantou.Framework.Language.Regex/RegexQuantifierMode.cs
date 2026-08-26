namespace Meziantou.Framework.Language.Regex;

/// <summary>How a quantifier backtracks.</summary>
public enum RegexQuantifierMode
{
    /// <summary>Matches as much as possible, then gives characters back.</summary>
    Greedy,

    /// <summary>Matches as little as possible, written with a trailing <c>?</c>.</summary>
    Lazy,

    /// <summary>Matches as much as possible and never gives characters back, written with a trailing <c>+</c>.</summary>
    Possessive,
}
