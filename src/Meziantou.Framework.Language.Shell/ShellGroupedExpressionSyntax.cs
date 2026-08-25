namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a parenthesized expression, <c>( ... )</c>, inside arithmetic or a conditional.</summary>
public sealed class ShellGroupedExpressionSyntax : ShellExpressionSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public ShellGroupedExpressionSyntax(ShellSyntaxToken openParenToken, ShellExpressionSyntax expression, ShellSyntaxToken closeParenToken)
        : base(
            ShellSyntaxKind.GroupedExpression,
            openParenToken.ToFullString() + expression.ToFullString() + closeParenToken.ToFullString(),
            openParenToken.FullSpan.Start,
            [openParenToken, closeParenToken])
    {
        OpenParenToken = openParenToken;
        Expression = expression;
        CloseParenToken = closeParenToken;
        _childNodes = [expression];
    }

    public ShellSyntaxToken OpenParenToken { get; }
    public ShellExpressionSyntax Expression { get; }
    public ShellSyntaxToken CloseParenToken { get; }
    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitGroupedExpression(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitGroupedExpression(this);
}
