namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a lookahead or lookbehind, <c>(?=…)</c>, <c>(?!…)</c>, <c>(?&lt;=…)</c>, or <c>(?&lt;!…)</c>.</summary>
public sealed partial class RegexLookaroundSyntax : RegexGroupSyntax
{
    /// <summary>The direction and polarity of the assertion.</summary>
    /// <remarks>PCRE's alpha assertions, such as <c>(*nlb:…)</c>, report the same kinds as their symbolic spellings.</remarks>
    public RegexLookaroundKind LookaroundKind => GroupKindToken.Text switch
    {
        "?!" or "*nla:" or "*negative_lookahead:" => RegexLookaroundKind.NegativeLookahead,
        "?<=" or "*plb:" or "*positive_lookbehind:" or "*naplb:" or "*non_atomic_positive_lookbehind:" => RegexLookaroundKind.PositiveLookbehind,
        "?<!" or "*nlb:" or "*negative_lookbehind:" => RegexLookaroundKind.NegativeLookbehind,
        _ => RegexLookaroundKind.PositiveLookahead,
    };

    /// <summary>Returns <see langword="true"/> when the assertion looks backwards.</summary>
    public bool IsLookbehind => LookaroundKind is RegexLookaroundKind.PositiveLookbehind or RegexLookaroundKind.NegativeLookbehind;

    /// <summary>Returns <see langword="true"/> when the assertion must fail for the pattern to match.</summary>
    public bool IsNegative => LookaroundKind is RegexLookaroundKind.NegativeLookahead or RegexLookaroundKind.NegativeLookbehind;
}
