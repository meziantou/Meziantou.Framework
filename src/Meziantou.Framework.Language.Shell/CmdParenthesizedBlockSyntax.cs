namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a parenthesized block of statements, <c>( ... )</c>.</summary>
public sealed class CmdParenthesizedBlockSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public CmdParenthesizedBlockSyntax(
        ShellSyntaxToken openParenToken,
        ShellStatementListSyntax statements,
        ShellSyntaxToken closeParenToken)
        : base(
            ShellSyntaxKind.CmdParenthesizedBlock,
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

    /// <summary>The statements in the block.</summary>
    public ShellStatementListSyntax Statements { get; }

    /// <summary>The closing parenthesis.</summary>
    public ShellSyntaxToken CloseParenToken { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitCmdBlock(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitCmdBlock(this);
}
