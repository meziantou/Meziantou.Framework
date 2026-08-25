namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents the <c>else</c> clause of an <see cref="PosixIfStatementSyntax"/>.</summary>
public sealed class PosixElseClauseSyntax : ShellSyntaxNode
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PosixElseClauseSyntax(ShellSyntaxToken elseKeyword, ShellStatementListSyntax body)
        : base(
            ShellSyntaxKind.PosixElseClause,
            elseKeyword?.ToFullString() + body?.ToFullString(),
            elseKeyword?.FullSpan.Start ?? 0,
            [elseKeyword!])
    {
        ElseKeyword = elseKeyword!;
        Body = body!;
        _childNodes = [body!];
    }

    public ShellSyntaxToken ElseKeyword { get; }
    public ShellStatementListSyntax Body { get; }
    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitElseClause(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitElseClause(this);
}
