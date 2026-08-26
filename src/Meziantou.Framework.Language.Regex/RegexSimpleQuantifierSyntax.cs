namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a <c>*</c>, <c>+</c>, or <c>?</c> quantifier, with an optional lazy or possessive modifier.</summary>
public sealed class RegexSimpleQuantifierSyntax : RegexQuantifierSyntax
{
    public RegexSimpleQuantifierSyntax(RegexSyntaxToken operatorToken, RegexSyntaxToken? modifierToken)
        : base(RegexSyntaxKind.SimpleQuantifier, [operatorToken, modifierToken], Part(operatorToken), Part(modifierToken))
    {
        OperatorToken = operatorToken;
        ModifierToken = modifierToken;
    }

    /// <summary>The <c>*</c>, <c>+</c>, or <c>?</c> itself.</summary>
    public RegexSyntaxToken OperatorToken { get; }

    public override RegexSyntaxToken? ModifierToken { get; }

    public override int MinCount => OperatorToken.Text == "+" ? 1 : 0;

    public override int? MaxCount => OperatorToken.Text == "?" ? 1 : null;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitSimpleQuantifier(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitSimpleQuantifier(this);
}
