namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents the group reference that a conditional tests, the <c>(1)</c> or <c>(name)</c> of <c>(?(1)yes|no)</c>.</summary>
public sealed class RegexConditionalReferenceSyntax : RegexSyntaxNode
{
    public RegexConditionalReferenceSyntax(RegexSyntaxToken openParenToken, RegexSyntaxToken? nameToken, RegexSyntaxToken closeParenToken)
        : base(RegexSyntaxKind.ConditionalReference, [openParenToken, nameToken, closeParenToken], Part(openParenToken), Part(nameToken), Part(closeParenToken))
    {
        OpenParenToken = openParenToken;
        NameToken = nameToken;
        CloseParenToken = closeParenToken;
    }

    public RegexSyntaxToken OpenParenToken { get; }

    public RegexSyntaxToken? NameToken { get; }

    public RegexSyntaxToken CloseParenToken { get; }

    /// <summary>The group name or number being tested.</summary>
    public string Name => NameToken?.Text ?? string.Empty;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitConditionalReference(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitConditionalReference(this);
}
