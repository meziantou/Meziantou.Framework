namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a conditional alternation, <c>(?(1)yes|no)</c> or <c>(?(?=a)yes|no)</c>.</summary>
public sealed class RegexConditionalSyntax : RegexGroupSyntax
{
    private readonly IReadOnlyList<RegexSyntaxNode> _childNodes;

    public RegexConditionalSyntax(RegexSyntaxToken openParenToken, RegexSyntaxToken questionToken, RegexSyntaxNode? condition, RegexAlternationSyntax alternation, RegexSyntaxToken closeParenToken)
        : base(RegexSyntaxKind.Conditional, [openParenToken, questionToken, closeParenToken], Part(openParenToken), Part(questionToken), Part(condition), Part(alternation), Part(closeParenToken))
    {
        OpenParenToken = openParenToken;
        QuestionToken = questionToken;
        Condition = condition;
        Alternation = alternation;
        CloseParenToken = closeParenToken;
        _childNodes = Children(condition, alternation);
    }

    public RegexSyntaxToken QuestionToken { get; }

    /// <summary>The group reference or the assertion the conditional tests.</summary>
    public RegexSyntaxNode? Condition { get; }

    /// <summary>The branches. The first is taken when the condition holds, the second when it does not.</summary>
    public RegexAlternationSyntax Alternation { get; }

    public override RegexSyntaxToken OpenParenToken { get; }

    public override RegexSyntaxToken CloseParenToken { get; }

    public override IReadOnlyList<RegexSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitConditional(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitConditional(this);
}
