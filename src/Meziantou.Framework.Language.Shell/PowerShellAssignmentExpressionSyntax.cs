namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an assignment such as <c>$x = 1</c> or <c>$x += 1</c>.</summary>
public sealed class PowerShellAssignmentExpressionSyntax : ShellExpressionSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellAssignmentExpressionSyntax(
        ShellExpressionSyntax target,
        ShellSyntaxToken operatorToken,
        ShellSyntaxNode value)
        : base(
            ShellSyntaxKind.PowerShellAssignmentExpression,
            target.ToFullString() + operatorToken.ToFullString() + value.ToFullString(),
            target.FullSpan.Start,
            [operatorToken])
    {
        Target = target;
        OperatorToken = operatorToken;
        Value = value;
        _childNodes = [.. SingleNode(Target), .. SingleNode(Value)];
    }

    /// <summary>The assignment target.</summary>
    public ShellExpressionSyntax Target { get; }

    /// <summary>The assignment operator.</summary>
    public ShellSyntaxToken OperatorToken { get; }

    /// <summary>The assigned value.</summary>
    public ShellSyntaxNode Value { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitAssignmentExpression(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitAssignmentExpression(this);
}
