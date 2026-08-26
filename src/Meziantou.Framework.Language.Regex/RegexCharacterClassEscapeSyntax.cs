namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a shorthand class escape: <c>\d</c>, <c>\D</c>, <c>\w</c>, <c>\W</c>, <c>\s</c>, or <c>\S</c>.</summary>
public sealed class RegexCharacterClassEscapeSyntax : RegexAtomSyntax
{
    public RegexCharacterClassEscapeSyntax(RegexSyntaxToken escapeToken)
        : base(RegexSyntaxKind.CharacterClassEscape, [escapeToken], Part(escapeToken))
    {
        EscapeToken = escapeToken;
    }

    public RegexSyntaxToken EscapeToken { get; }

    /// <summary>Returns <see langword="true"/> when the escape is the negated form, spelled with a capital letter.</summary>
    /// <remarks>
    /// Only the letters that come in pairs. <c>\R</c> and <c>\X</c> are capitals without a lower-case counterpart, so
    /// they are not negations of anything.
    /// </remarks>
    public bool IsNegated => EscapeToken.Text is [_, 'D' or 'S' or 'W' or 'H' or 'V'];

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitCharacterClassEscape(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitCharacterClassEscape(this);
}
