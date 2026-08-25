namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an <c>if</c> statement in its <c>errorlevel</c>, <c>defined</c>, <c>exist</c>, or comparison form.</summary>
public sealed class CmdIfStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public CmdIfStatementSyntax(
        ShellSyntaxToken ifKeyword,
        ShellSyntaxToken? caseInsensitiveToken,
        ShellSyntaxToken? notKeyword,
        ShellExpressionSyntax condition,
        ShellStatementSyntax body,
        CmdElseClauseSyntax? elseClause)
        : base(
            ShellSyntaxKind.CmdIfStatement,
            ifKeyword.ToFullString() + (caseInsensitiveToken?.ToFullString() ?? string.Empty) + (notKeyword?.ToFullString() ?? string.Empty) + condition.ToFullString() + body.ToFullString() + (elseClause?.ToFullString() ?? string.Empty),
            ifKeyword.FullSpan.Start,
            BuildTokens(ifKeyword, caseInsensitiveToken, notKeyword))
    {
        IfKeyword = ifKeyword;
        CaseInsensitiveToken = caseInsensitiveToken;
        NotKeyword = notKeyword;
        Condition = condition;
        Body = body;
        ElseClause = elseClause;
        _childNodes = [.. SingleNode(Condition), .. SingleNode(Body), .. OptionalNode(ElseClause)];
    }

    /// <summary>The <c>if</c> keyword.</summary>
    public ShellSyntaxToken IfKeyword { get; }

    /// <summary>The <c>/i</c> switch, when present.</summary>
    public ShellSyntaxToken? CaseInsensitiveToken { get; }

    /// <summary>The <c>not</c> keyword, when present.</summary>
    public ShellSyntaxToken? NotKeyword { get; }

    /// <summary>
    /// The condition. A <see cref="ShellUnaryExpressionSyntax"/> for the <c>errorlevel</c>, <c>defined</c>,
    /// <c>exist</c>, and <c>cmdextversion</c> forms, a <see cref="ShellBinaryExpressionSyntax"/> for the <c>==</c> and
    /// <c>EQU</c> comparisons, and a lone <see cref="ShellOperandExpressionSyntax"/> for text matching none of them.
    /// </summary>
    public ShellExpressionSyntax Condition { get; }

    /// <summary>The statement run when the condition holds.</summary>
    public ShellStatementSyntax Body { get; }

    /// <summary>The <c>else</c> clause, when present.</summary>
    public CmdElseClauseSyntax? ElseClause { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    /// <summary>Returns <see langword="true"/> when the comparison ignores case.</summary>
    public bool IsCaseInsensitive => CaseInsensitiveToken is not null;

    /// <summary>Returns <see langword="true"/> when the condition is negated.</summary>
    public bool IsNegated => NotKeyword is not null;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitCmdIf(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitCmdIf(this);

    private static List<ShellSyntaxToken> BuildTokens(
        ShellSyntaxToken ifKeyword,
        ShellSyntaxToken? caseInsensitiveToken,
        ShellSyntaxToken? notKeyword)
    {
        var tokens = new List<ShellSyntaxToken>();
        tokens.Add(ifKeyword);
        if (caseInsensitiveToken is not null)
        {
            tokens.Add(caseInsensitiveToken);
        }

        if (notKeyword is not null)
        {
            tokens.Add(notKeyword);
        }

        return tokens;
    }
}
