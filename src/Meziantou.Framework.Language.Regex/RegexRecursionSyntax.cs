namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a PCRE recursion construct such as <c>(?R)</c> or <c>(?1)</c>.</summary>
public sealed class RegexRecursionSyntax : RegexAtomSyntax
{
    public RegexRecursionSyntax(RegexSyntaxToken openParenToken, RegexSyntaxToken questionToken, RegexSyntaxToken? targetToken, RegexSyntaxToken closeParenToken)
        : base(RegexSyntaxKind.Recursion, [openParenToken, questionToken, targetToken, closeParenToken], Part(openParenToken), Part(questionToken), Part(targetToken), Part(closeParenToken))
    {
        OpenParenToken = openParenToken;
        QuestionToken = questionToken;
        TargetToken = targetToken;
        CloseParenToken = closeParenToken;
    }

    public RegexSyntaxToken OpenParenToken { get; }

    public RegexSyntaxToken QuestionToken { get; }

    /// <summary>The <c>R</c> or the group number the construct recurses into.</summary>
    public RegexSyntaxToken? TargetToken { get; }

    public RegexSyntaxToken CloseParenToken { get; }

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitRecursion(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitRecursion(this);
}
