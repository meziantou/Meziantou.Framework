namespace Meziantou.Framework.Language.Shell;

/// <summary>Wraps an expression used where a statement is expected.</summary>
public sealed class PowerShellExpressionStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellExpressionStatementSyntax(
        ShellSyntaxNode expression,
        IReadOnlyList<ShellRedirectionSyntax>? redirections)
        : base(
            ShellSyntaxKind.PowerShellExpressionStatement,
            expression.ToFullString() + BuildFullText(redirections ?? []),
            expression.FullSpan.Start,
            null)
    {
        Expression = expression;
        Redirections = redirections ?? [];
        _childNodes = [.. SingleNode(Expression), .. (Redirections as IEnumerable<ShellSyntaxNode>)];
    }

    /// <summary>The expression.</summary>
    public ShellSyntaxNode Expression { get; }

    /// <summary>Redirections applied to the expression.</summary>
    public IReadOnlyList<ShellRedirectionSyntax> Redirections { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitExpressionStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitExpressionStatement(this);
}
