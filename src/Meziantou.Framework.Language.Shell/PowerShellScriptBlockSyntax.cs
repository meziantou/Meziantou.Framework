namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a script block, <c>{ ... }</c>.</summary>
public sealed class PowerShellScriptBlockSyntax : ShellExpressionSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellScriptBlockSyntax(
        ShellSyntaxToken openBraceToken,
        ShellStatementListSyntax statements,
        ShellSyntaxToken closeBraceToken)
        : base(
            ShellSyntaxKind.PowerShellScriptBlock,
            openBraceToken.ToFullString() + statements.ToFullString() + closeBraceToken.ToFullString(),
            openBraceToken.FullSpan.Start,
            [openBraceToken, closeBraceToken])
    {
        OpenBraceToken = openBraceToken;
        Statements = statements;
        CloseBraceToken = closeBraceToken;
        _childNodes = [.. SingleNode(Statements)];
    }

    /// <summary>The opening brace.</summary>
    public ShellSyntaxToken OpenBraceToken { get; }

    /// <summary>The statements in the block.</summary>
    public ShellStatementListSyntax Statements { get; }

    /// <summary>The closing brace.</summary>
    public ShellSyntaxToken CloseBraceToken { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public PowerShellScriptBlockSyntax WithStatements(ShellStatementListSyntax statements)
    {
        ArgumentNullException.ThrowIfNull(statements);
        if (ReferenceEquals(statements, Statements))
            return this;

        return new PowerShellScriptBlockSyntax(OpenBraceToken, statements, CloseBraceToken);
    }

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitScriptBlock(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitScriptBlock(this);
}
