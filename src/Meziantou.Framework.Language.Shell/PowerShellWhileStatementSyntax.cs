namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>while</c> loop.</summary>
public sealed class PowerShellWhileStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellWhileStatementSyntax(
        ShellSyntaxToken whileKeyword,
        ShellSyntaxToken openParenToken,
        ShellStatementListSyntax condition,
        ShellSyntaxToken closeParenToken,
        PowerShellScriptBlockSyntax body)
        : base(
            ShellSyntaxKind.PowerShellWhileStatement,
            whileKeyword.ToFullString() + openParenToken.ToFullString() + condition.ToFullString() + closeParenToken.ToFullString() + body.ToFullString(),
            whileKeyword.FullSpan.Start,
            [whileKeyword, openParenToken, closeParenToken])
    {
        WhileKeyword = whileKeyword;
        OpenParenToken = openParenToken;
        Condition = condition;
        CloseParenToken = closeParenToken;
        Body = body;
        _childNodes = [.. SingleNode(Condition), .. SingleNode(Body)];
    }

    /// <summary>The <c>while</c> keyword.</summary>
    public ShellSyntaxToken WhileKeyword { get; }

    /// <summary>The opening parenthesis.</summary>
    public ShellSyntaxToken OpenParenToken { get; }

    /// <summary>The loop condition.</summary>
    public ShellStatementListSyntax Condition { get; }

    /// <summary>The closing parenthesis.</summary>
    public ShellSyntaxToken CloseParenToken { get; }

    /// <summary>The loop body.</summary>
    public PowerShellScriptBlockSyntax Body { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitPowerShellWhileStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitPowerShellWhileStatement(this);
}
