namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an <c>if</c> statement with its <c>elseif</c> and <c>else</c> clauses.</summary>
public sealed class PowerShellIfStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellIfStatementSyntax(
        ShellSyntaxToken ifKeyword,
        ShellSyntaxToken openParenToken,
        ShellStatementListSyntax condition,
        ShellSyntaxToken closeParenToken,
        PowerShellScriptBlockSyntax body,
        IReadOnlyList<PowerShellElseIfClauseSyntax>? elseIfClauses,
        PowerShellElseClauseSyntax? elseClause)
        : base(
            ShellSyntaxKind.PowerShellIfStatement,
            ifKeyword.ToFullString() + openParenToken.ToFullString() + condition.ToFullString() + closeParenToken.ToFullString() + body.ToFullString() + BuildFullText(elseIfClauses ?? []) + (elseClause?.ToFullString() ?? string.Empty),
            ifKeyword.FullSpan.Start,
            [ifKeyword, openParenToken, closeParenToken])
    {
        IfKeyword = ifKeyword;
        OpenParenToken = openParenToken;
        Condition = condition;
        CloseParenToken = closeParenToken;
        Body = body;
        ElseIfClauses = elseIfClauses ?? [];
        ElseClause = elseClause;
        _childNodes = [.. SingleNode(Condition), .. SingleNode(Body), .. (ElseIfClauses as IEnumerable<ShellSyntaxNode>), .. OptionalNode(ElseClause)];
    }

    /// <summary>The <c>if</c> keyword.</summary>
    public ShellSyntaxToken IfKeyword { get; }

    /// <summary>The opening parenthesis.</summary>
    public ShellSyntaxToken OpenParenToken { get; }

    /// <summary>The condition.</summary>
    public ShellStatementListSyntax Condition { get; }

    /// <summary>The closing parenthesis.</summary>
    public ShellSyntaxToken CloseParenToken { get; }

    /// <summary>The body.</summary>
    public PowerShellScriptBlockSyntax Body { get; }

    /// <summary>The <c>elseif</c> clauses.</summary>
    public IReadOnlyList<PowerShellElseIfClauseSyntax> ElseIfClauses { get; }

    /// <summary>The <c>else</c> clause, when present.</summary>
    public PowerShellElseClauseSyntax? ElseClause { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitPowerShellIfStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitPowerShellIfStatement(this);
}
