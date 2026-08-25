namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an <c>elseif</c> clause.</summary>
public sealed class PowerShellElseIfClauseSyntax : ShellSyntaxNode
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellElseIfClauseSyntax(
        ShellSyntaxToken elseIfKeyword,
        ShellSyntaxToken openParenToken,
        ShellStatementListSyntax condition,
        ShellSyntaxToken closeParenToken,
        PowerShellScriptBlockSyntax body)
        : base(
            ShellSyntaxKind.PowerShellElseIfClause,
            elseIfKeyword.ToFullString() + openParenToken.ToFullString() + condition.ToFullString() + closeParenToken.ToFullString() + body.ToFullString(),
            elseIfKeyword.FullSpan.Start,
            [elseIfKeyword, openParenToken, closeParenToken])
    {
        ElseIfKeyword = elseIfKeyword;
        OpenParenToken = openParenToken;
        Condition = condition;
        CloseParenToken = closeParenToken;
        Body = body;
        _childNodes = [.. SingleNode(Condition), .. SingleNode(Body)];
    }

    /// <summary>The <c>elseif</c> keyword.</summary>
    public ShellSyntaxToken ElseIfKeyword { get; }

    /// <summary>The opening parenthesis.</summary>
    public ShellSyntaxToken OpenParenToken { get; }

    /// <summary>The condition.</summary>
    public ShellStatementListSyntax Condition { get; }

    /// <summary>The closing parenthesis.</summary>
    public ShellSyntaxToken CloseParenToken { get; }

    /// <summary>The body.</summary>
    public PowerShellScriptBlockSyntax Body { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitElseIfClause(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitElseIfClause(this);
}
