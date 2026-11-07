using System.Globalization;

namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a <c>{n}</c>, <c>{n,}</c>, or <c>{n,m}</c> quantifier, with an optional lazy or possessive modifier.</summary>
public sealed partial class RegexRangeQuantifierSyntax : RegexQuantifierSyntax
{
    public override int MinCount => ParseBound(MinToken) ?? 0;

    /// <summary>Gets the upper bound, or <see langword="null"/> when the quantifier is unbounded.</summary>
    /// <remarks>
    /// <c>{2}</c> has no comma, so its upper bound is its lower bound; <c>{2,}</c> has a comma and no second number,
    /// so it is unbounded.
    /// </remarks>
    public override int? MaxCount => CommaToken.IsKind(SyntaxKind.None) ? MinCount : ParseBound(MaxToken);

    private static int? ParseBound(SyntaxToken token)
    {
        if (token.Text.Length == 0)
            return null;

        // The parser clamps a bound that does not fit, and still consumes every digit, so the text can be longer
        // than an int. ValueText carries the clamped value.
        return int.TryParse(token.ValueText, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : int.MaxValue;
    }
}
