namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an <c>elif ... then ...</c> clause of an <see cref="PosixIfStatementSyntax"/>.</summary>
public sealed class PosixElifClauseSyntax : ShellSyntaxNode
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PosixElifClauseSyntax(ShellSyntaxToken elifKeyword, ShellStatementListSyntax condition, ShellSyntaxToken thenKeyword, ShellStatementListSyntax body)
        : base(
            ShellSyntaxKind.PosixElifClause,
            elifKeyword?.ToFullString() + condition?.ToFullString() + thenKeyword?.ToFullString() + body?.ToFullString(),
            elifKeyword?.FullSpan.Start ?? 0,
            [elifKeyword!, thenKeyword!])
    {
        ElifKeyword = elifKeyword!;
        Condition = condition!;
        ThenKeyword = thenKeyword!;
        Body = body!;
        _childNodes = [condition!, body!];
    }

    public ShellSyntaxToken ElifKeyword { get; }
    public ShellStatementListSyntax Condition { get; }
    public ShellSyntaxToken ThenKeyword { get; }
    public ShellStatementListSyntax Body { get; }
    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitElifClause(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitElifClause(this);
}
