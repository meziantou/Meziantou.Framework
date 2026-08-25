namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a binary expression such as <c>$a -eq $b</c>, <c>1..10</c>, or <c>$a + $b</c>.</summary>
public sealed class PowerShellBinaryExpressionSyntax : ShellExpressionSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellBinaryExpressionSyntax(
        ShellSyntaxKind kind,
        ShellExpressionSyntax left,
        ShellSyntaxToken operatorToken,
        ShellExpressionSyntax right)
        : base(
            kind,
            left.ToFullString() + operatorToken.ToFullString() + right.ToFullString(),
            left.FullSpan.Start,
            [operatorToken])
    {
        Left = left;
        OperatorToken = operatorToken;
        Right = right;
        _childNodes = [.. SingleNode(Left), .. SingleNode(Right)];
    }

    /// <summary>The left operand.</summary>
    public ShellExpressionSyntax Left { get; }

    /// <summary>The operator.</summary>
    public ShellSyntaxToken OperatorToken { get; }

    /// <summary>The right operand.</summary>
    public ShellExpressionSyntax Right { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitBinaryExpression(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitBinaryExpression(this);
}
