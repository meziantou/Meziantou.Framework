namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a lookahead or lookbehind, <c>(?=…)</c>, <c>(?!…)</c>, <c>(?&lt;=…)</c>, or <c>(?&lt;!…)</c>.</summary>
public sealed partial class RegexLookaroundSyntax : RegexGroupSyntax
{
    /// <summary>The direction and polarity of the assertion.</summary>
    public RegexLookaroundKind LookaroundKind => GroupKindToken.Text switch
    {
        "?!" => RegexLookaroundKind.NegativeLookahead,
        "?<=" => RegexLookaroundKind.PositiveLookbehind,
        "?<!" => RegexLookaroundKind.NegativeLookbehind,
        _ => RegexLookaroundKind.PositiveLookahead,
    };

    /// <summary>Returns <see langword="true"/> when the assertion looks backwards.</summary>
    public bool IsLookbehind => LookaroundKind is RegexLookaroundKind.PositiveLookbehind or RegexLookaroundKind.NegativeLookbehind;

    /// <summary>Returns <see langword="true"/> when the assertion must fail for the pattern to match.</summary>
    public bool IsNegative => LookaroundKind is RegexLookaroundKind.NegativeLookahead or RegexLookaroundKind.NegativeLookbehind;
}
