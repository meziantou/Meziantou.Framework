namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a single ordinary character that matches itself.</summary>
public sealed class RegexLiteralSyntax : RegexAtomSyntax
{
    public RegexLiteralSyntax(RegexSyntaxToken literalToken)
        : base(RegexSyntaxKind.Literal, [literalToken], Part(literalToken))
    {
        LiteralToken = literalToken;
    }

    public RegexSyntaxToken LiteralToken { get; }

    /// <summary>The character this literal matches.</summary>
    public char Value => LiteralToken.Text.Length > 0 ? LiteralToken.Text[0] : '\0';

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitLiteral(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitLiteral(this);
}
