namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a command substitution such as <c>$(...)</c> or a backquoted command.</summary>
public sealed class ShellCommandSubstitutionSyntax : ShellWordPartSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public ShellCommandSubstitutionSyntax(ShellSyntaxToken openToken, ShellStatementListSyntax statements, ShellSyntaxToken closeToken)
        : base(
            ShellSyntaxKind.CommandSubstitution,
            BuildText(openToken, statements, closeToken),
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

    /// <summary>Returns <see langword="true"/> for the legacy backquoted form.</summary>
    public bool IsBackquoted => OpenToken.Kind == ShellSyntaxKind.BacktickToken;

    public ShellCommandSubstitutionSyntax WithStatements(ShellStatementListSyntax statements)
    {
        ArgumentNullException.ThrowIfNull(statements);
        if (ReferenceEquals(statements, Statements))
            return this;

        return new ShellCommandSubstitutionSyntax(OpenToken, statements, CloseToken);
    }

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitCommandSubstitution(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitCommandSubstitution(this);

    private static string BuildText(ShellSyntaxToken openToken, ShellStatementListSyntax statements, ShellSyntaxToken closeToken)
    {
        ArgumentNullException.ThrowIfNull(openToken);
        ArgumentNullException.ThrowIfNull(statements);
        ArgumentNullException.ThrowIfNull(closeToken);

        return openToken.ToFullString() + statements.ToFullString() + closeToken.ToFullString();
    }
}
