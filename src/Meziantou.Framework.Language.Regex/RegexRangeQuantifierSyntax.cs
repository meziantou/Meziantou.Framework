using System.Globalization;

namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a <c>{n}</c>, <c>{n,}</c>, or <c>{n,m}</c> quantifier, with an optional lazy or possessive modifier.</summary>
public sealed class RegexRangeQuantifierSyntax : RegexQuantifierSyntax
{
    public RegexRangeQuantifierSyntax(RegexSyntaxToken openBraceToken, RegexSyntaxToken minToken, RegexSyntaxToken? commaToken, RegexSyntaxToken? maxToken, RegexSyntaxToken closeBraceToken, RegexSyntaxToken? modifierToken)
        : base(RegexSyntaxKind.RangeQuantifier, [openBraceToken, minToken, commaToken, maxToken, closeBraceToken, modifierToken], Part(openBraceToken), Part(minToken), Part(commaToken), Part(maxToken), Part(closeBraceToken), Part(modifierToken))
    {
        OpenBraceToken = openBraceToken;
        MinToken = minToken;
        CommaToken = commaToken;
        MaxToken = maxToken;
        CloseBraceToken = closeBraceToken;
        ModifierToken = modifierToken;
    }

    public RegexSyntaxToken OpenBraceToken { get; }

    /// <summary>The digits of the lower bound.</summary>
    public RegexSyntaxToken MinToken { get; }

    public RegexSyntaxToken? CommaToken { get; }

    /// <summary>The digits of the upper bound, absent for <c>{n}</c> and <c>{n,}</c>.</summary>
    public RegexSyntaxToken? MaxToken { get; }

    public RegexSyntaxToken CloseBraceToken { get; }

    public override RegexSyntaxToken? ModifierToken { get; }

    public override int MinCount => ParseBound(MinToken) ?? 0;

    /// <summary>
    /// The upper bound, or <see langword="null"/> when the quantifier is unbounded.
    /// </summary>
    /// <remarks>
    /// <c>{2}</c> has no comma, so its upper bound is its lower bound; <c>{2,}</c> has a comma and no second number,
    /// so it is unbounded.
    /// </remarks>
    public override int? MaxCount => CommaToken is null ? MinCount : ParseBound(MaxToken);

    private static int? ParseBound(RegexSyntaxToken? token)
    {
        if (token is null || token.Text.Length == 0)
            return null;

        // The parser clamps a bound that does not fit, and still consumes every digit, so the text can be longer
        // than an int. ValueText carries the clamped value.
        return int.TryParse(token.ValueText, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : int.MaxValue;
    }

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitRangeQuantifier(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitRangeQuantifier(this);
}
