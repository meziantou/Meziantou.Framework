namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents the PowerShell 7 ternary expression, <c>$c ? $a : $b</c>.</summary>
public sealed class PowerShellTernaryExpressionSyntax : ShellExpressionSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellTernaryExpressionSyntax(
        ShellExpressionSyntax condition,
        ShellSyntaxToken questionToken,
        ShellExpressionSyntax whenTrue,
        ShellSyntaxToken colonToken,
        ShellExpressionSyntax whenFalse)
        : base(
            ShellSyntaxKind.PowerShellTernaryExpression,
            condition.ToFullString() + questionToken.ToFullString() + whenTrue.ToFullString() + colonToken.ToFullString() + whenFalse.ToFullString(),
            condition.FullSpan.Start,
            [questionToken, colonToken])
    {
        Condition = condition;
        QuestionToken = questionToken;
        WhenTrue = whenTrue;
        ColonToken = colonToken;
        WhenFalse = whenFalse;
        _childNodes = [.. SingleNode(Condition), .. SingleNode(WhenTrue), .. SingleNode(WhenFalse)];
    }

    /// <summary>The condition.</summary>
    public ShellExpressionSyntax Condition { get; }

    /// <summary>The <c>?</c> token.</summary>
    public ShellSyntaxToken QuestionToken { get; }

    /// <summary>The value used when the condition is true.</summary>
    public ShellExpressionSyntax WhenTrue { get; }

    /// <summary>The <c>:</c> token.</summary>
    public ShellSyntaxToken ColonToken { get; }

    /// <summary>The value used when the condition is false.</summary>
    public ShellExpressionSyntax WhenFalse { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitTernaryExpression(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitTernaryExpression(this);
}
