namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>try</c> statement with its <c>catch</c> and <c>finally</c> clauses.</summary>
public sealed class PowerShellTryStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellTryStatementSyntax(
        ShellSyntaxToken tryKeyword,
        PowerShellScriptBlockSyntax body,
        IReadOnlyList<PowerShellCatchClauseSyntax>? catchClauses,
        PowerShellFinallyClauseSyntax? finallyClause)
        : base(
            ShellSyntaxKind.PowerShellTryStatement,
            tryKeyword.ToFullString() + body.ToFullString() + BuildFullText(catchClauses ?? []) + (finallyClause?.ToFullString() ?? string.Empty),
            tryKeyword.FullSpan.Start,
            [tryKeyword])
    {
        TryKeyword = tryKeyword;
        Body = body;
        CatchClauses = catchClauses ?? [];
        FinallyClause = finallyClause;
        _childNodes = [.. SingleNode(Body), .. (CatchClauses as IEnumerable<ShellSyntaxNode>), .. OptionalNode(FinallyClause)];
    }

    /// <summary>The <c>try</c> keyword.</summary>
    public ShellSyntaxToken TryKeyword { get; }

    /// <summary>The protected body.</summary>
    public PowerShellScriptBlockSyntax Body { get; }

    /// <summary>The catch clauses.</summary>
    public IReadOnlyList<PowerShellCatchClauseSyntax> CatchClauses { get; }

    /// <summary>The finally clause, when present.</summary>
    public PowerShellFinallyClauseSyntax? FinallyClause { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitTryStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitTryStatement(this);
}
