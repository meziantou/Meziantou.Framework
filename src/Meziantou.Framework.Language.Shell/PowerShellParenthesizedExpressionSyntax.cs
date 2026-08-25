namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a parenthesized expression or a pipeline used as a value, <c>( ... )</c>.</summary>
public sealed class PowerShellParenthesizedExpressionSyntax : ShellExpressionSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellParenthesizedExpressionSyntax(
        ShellSyntaxToken openParenToken,
        ShellStatementListSyntax statements,
        ShellSyntaxToken closeParenToken)
        : base(
            ShellSyntaxKind.PowerShellParenthesizedExpression,
            openParenToken.ToFullString() + statements.ToFullString() + closeParenToken.ToFullString(),
            openParenToken.FullSpan.Start,
            [openParenToken, closeParenToken])
    {
        OpenParenToken = openParenToken;
        Statements = statements;
        CloseParenToken = closeParenToken;
        _childNodes = [.. SingleNode(Statements)];
    }

    /// <summary>The opening parenthesis.</summary>
    public ShellSyntaxToken OpenParenToken { get; }

    /// <summary>The statements inside the parentheses.</summary>
    public ShellStatementListSyntax Statements { get; }

    /// <summary>The closing parenthesis.</summary>
    public ShellSyntaxToken CloseParenToken { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitParenthesizedExpression(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitParenthesizedExpression(this);
}
