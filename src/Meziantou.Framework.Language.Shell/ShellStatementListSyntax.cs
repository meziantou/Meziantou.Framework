namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a sequence of statements separated by <c>;</c>, <c>&amp;</c>, or line breaks.</summary>
public sealed class ShellStatementListSyntax : ShellSyntaxNode
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public ShellStatementListSyntax(IReadOnlyList<ShellStatementSyntax>? statements, IReadOnlyList<ShellSyntaxToken>? separatorTokens = null)
        : base(
            ShellSyntaxKind.StatementList,
            SeparatedNodes.BuildText(statements, separatorTokens),
            SeparatedNodes.GetFullStart(statements, separatorTokens),
            separatorTokens ?? [])
    {
        Statements = statements ?? [];
        SeparatorTokens = separatorTokens ?? [];
        _childNodes = [.. Statements];
    }

    public IReadOnlyList<ShellStatementSyntax> Statements { get; }

    /// <summary>The separator that follows each statement. <c>SeparatorTokens[i]</c> follows <c>Statements[i]</c>.</summary>
    public IReadOnlyList<ShellSyntaxToken> SeparatorTokens { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public ShellStatementListSyntax WithStatements(IEnumerable<ShellStatementSyntax>? statements)
    {
        var updated = statements?.ToArray() ?? [];
        if (updated.SequenceEqual(Statements))
            return this;

        return new ShellStatementListSyntax(updated, SeparatorTokens);
    }

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitStatementList(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitStatementList(this);
}
