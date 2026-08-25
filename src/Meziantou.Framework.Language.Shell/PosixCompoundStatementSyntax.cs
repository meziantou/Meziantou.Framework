namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a subshell, <c>( ... )</c>, or a brace group, <c>{ ...; }</c>.</summary>
public sealed class PosixCompoundStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PosixCompoundStatementSyntax(ShellSyntaxKind kind, ShellSyntaxToken openToken, ShellStatementListSyntax statements, ShellSyntaxToken closeToken)
        : base(
            kind,
            openToken?.ToFullString() + statements?.ToFullString() + closeToken?.ToFullString(),
            openToken?.FullSpan.Start ?? 0,
            [openToken!, closeToken!])
    {
        OpenToken = openToken!;
        Statements = statements!;
        CloseToken = closeToken!;
        _childNodes = [statements!];
    }

    public ShellSyntaxToken OpenToken { get; }
    public ShellStatementListSyntax Statements { get; }
    public ShellSyntaxToken CloseToken { get; }
    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    /// <summary>Returns <see langword="true"/> for <c>( ... )</c>, which runs in a child shell.</summary>
    public bool IsSubshell => Kind == ShellSyntaxKind.PosixSubshell;

    public PosixCompoundStatementSyntax WithStatements(ShellStatementListSyntax statements)
    {
        ArgumentNullException.ThrowIfNull(statements);
        if (ReferenceEquals(statements, Statements))
            return this;

        return new PosixCompoundStatementSyntax(Kind, OpenToken, statements, CloseToken);
    }

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitCompoundStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitCompoundStatement(this);
}
