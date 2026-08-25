namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a subexpression, <c>$( ... )</c>, or an array subexpression, <c>@( ... )</c>.</summary>
public sealed class PowerShellSubExpressionSyntax : ShellExpressionSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellSubExpressionSyntax(
        ShellSyntaxKind kind,
        ShellSyntaxToken openToken,
        ShellStatementListSyntax statements,
        ShellSyntaxToken closeParenToken)
        : base(
            kind,
            openToken.ToFullString() + statements.ToFullString() + closeParenToken.ToFullString(),
            openToken.FullSpan.Start,
            [openToken, closeParenToken])
    {
        OpenToken = openToken;
        Statements = statements;
        CloseParenToken = closeParenToken;
        _childNodes = [.. SingleNode(Statements)];
    }

    /// <summary>The <c>$(</c> or <c>@(</c> token.</summary>
    public ShellSyntaxToken OpenToken { get; }

    /// <summary>The statements inside.</summary>
    public ShellStatementListSyntax Statements { get; }

    /// <summary>The closing parenthesis.</summary>
    public ShellSyntaxToken CloseParenToken { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    /// <summary>Returns <see langword="true"/> for <c>@( ... )</c>, which always produces an array.</summary>
    public bool IsArray => Kind == ShellSyntaxKind.PowerShellArrayExpression;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitSubExpression(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitSubExpression(this);
}
