namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents the <c>.</c> metacharacter.</summary>
public sealed class RegexAnyCharacterSyntax : RegexAtomSyntax
{
    public RegexAnyCharacterSyntax(RegexSyntaxToken dotToken)
        : base(RegexSyntaxKind.AnyCharacter, [dotToken], Part(dotToken))
    {
        DotToken = dotToken;
    }

    public RegexSyntaxToken DotToken { get; }

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitAnyCharacter(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitAnyCharacter(this);
}
