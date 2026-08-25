namespace Meziantou.Framework.Language.Shell;

/// <summary>
/// Represents an infix expression such as <c>a + b</c> in arithmetic or <c>$x == y</c> in a conditional. The operator
/// is kept as a token rather than an enum, because the operator sets differ between the two grammars.
/// </summary>
public sealed class ShellBinaryExpressionSyntax : ShellExpressionSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public ShellBinaryExpressionSyntax(ShellExpressionSyntax left, ShellSyntaxToken operatorToken, ShellExpressionSyntax right)
        : base(
            ShellSyntaxKind.BinaryExpression,
            left.ToFullString() + operatorToken.ToFullString() + right.ToFullString(),
            left.FullSpan.Start,
            [operatorToken])
    {
        Left = left;
        OperatorToken = operatorToken;
        Right = right;
        _childNodes = [left, right];
    }

    public ShellExpressionSyntax Left { get; }
    public ShellSyntaxToken OperatorToken { get; }
    public ShellExpressionSyntax Right { get; }

    /// <summary>The operator text, such as <c>+</c>, <c>-eq</c>, or <c>=~</c>.</summary>
    public string OperatorText => OperatorToken.Text;

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitBinaryExpression(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitBinaryExpression(this);
}
