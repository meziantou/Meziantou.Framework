namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents the arithmetic ternary, <c>a ? b : c</c>.</summary>
public sealed class ShellConditionalExpressionSyntax : ShellExpressionSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public ShellConditionalExpressionSyntax(
        ShellExpressionSyntax condition,
        ShellSyntaxToken questionToken,
        ShellExpressionSyntax whenTrue,
        ShellSyntaxToken colonToken,
        ShellExpressionSyntax whenFalse)
        : base(
            ShellSyntaxKind.ConditionalExpression,
            condition.ToFullString() + questionToken.ToFullString() + whenTrue.ToFullString()
                + colonToken.ToFullString() + whenFalse.ToFullString(),
            condition.FullSpan.Start,
            [questionToken, colonToken])
    {
        Condition = condition;
        QuestionToken = questionToken;
        WhenTrue = whenTrue;
        ColonToken = colonToken;
        WhenFalse = whenFalse;
        _childNodes = [condition, whenTrue, whenFalse];
    }

    public ShellExpressionSyntax Condition { get; }
    public ShellSyntaxToken QuestionToken { get; }
    public ShellExpressionSyntax WhenTrue { get; }
    public ShellSyntaxToken ColonToken { get; }
    public ShellExpressionSyntax WhenFalse { get; }
    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitShellConditionalExpression(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitShellConditionalExpression(this);
}
