namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents an escape that stands for one character, such as <c>\n</c>, <c>\x41</c>, <c>\cA</c>, or <c>\052</c>.</summary>
public sealed class RegexCharacterEscapeSyntax : RegexAtomSyntax
{
    public RegexCharacterEscapeSyntax(RegexSyntaxToken escapeToken)
        : base(RegexSyntaxKind.CharacterEscape, [escapeToken], Part(escapeToken))
    {
        EscapeToken = escapeToken;
    }

    public RegexSyntaxToken EscapeToken { get; }

    /// <summary>The character the escape stands for.</summary>
    public string Value => EscapeToken.ValueText;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitCharacterEscape(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitCharacterEscape(this);
}
