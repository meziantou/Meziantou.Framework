namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an <c>if ... then ... fi</c> statement.</summary>
public sealed class PosixIfStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PosixIfStatementSyntax(
        ShellSyntaxToken ifKeyword,
        ShellStatementListSyntax condition,
        ShellSyntaxToken thenKeyword,
        ShellStatementListSyntax body,
        IReadOnlyList<PosixElifClauseSyntax>? elifClauses,
        PosixElseClauseSyntax? elseClause,
        ShellSyntaxToken fiKeyword)
        : base(
            ShellSyntaxKind.PosixIfStatement,
            ifKeyword?.ToFullString() + condition?.ToFullString() + thenKeyword?.ToFullString() + body?.ToFullString()
                + BuildFullText(elifClauses ?? []) + (elseClause?.ToFullString() ?? string.Empty) + fiKeyword?.ToFullString(),
            ifKeyword?.FullSpan.Start ?? 0,
            [ifKeyword!, thenKeyword!, fiKeyword!])
    {
        IfKeyword = ifKeyword!;
        Condition = condition!;
        ThenKeyword = thenKeyword!;
        Body = body!;
        ElifClauses = elifClauses ?? [];
        ElseClause = elseClause;
        FiKeyword = fiKeyword!;
        _childNodes = elseClause is null
            ? [condition!, body!, .. ElifClauses]
            : [condition!, body!, .. ElifClauses, elseClause];
    }

    public ShellSyntaxToken IfKeyword { get; }
    public ShellStatementListSyntax Condition { get; }
    public ShellSyntaxToken ThenKeyword { get; }
    public ShellStatementListSyntax Body { get; }
    public IReadOnlyList<PosixElifClauseSyntax> ElifClauses { get; }
    public PosixElseClauseSyntax? ElseClause { get; }
    public ShellSyntaxToken FiKeyword { get; }
    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitIfStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitIfStatement(this);
}
