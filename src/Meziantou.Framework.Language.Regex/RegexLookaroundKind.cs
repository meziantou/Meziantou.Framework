namespace Meziantou.Framework.Language.Regex;

/// <summary>Identifies the direction and polarity of a lookaround.</summary>
public enum RegexLookaroundKind
{
    /// <summary><c>(?=…)</c>.</summary>
    PositiveLookahead,

    /// <summary><c>(?!…)</c>.</summary>
    NegativeLookahead,

    /// <summary><c>(?&lt;=…)</c>.</summary>
    PositiveLookbehind,

    /// <summary><c>(?&lt;!…)</c>.</summary>
    NegativeLookbehind,
}
