namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a loop preceded by a label, <c>:outer while (...) { }</c>.</summary>
public sealed class PowerShellLabeledStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellLabeledStatementSyntax(
        ShellSyntaxToken labelToken,
        ShellStatementSyntax statement)
        : base(
            ShellSyntaxKind.PowerShellLabeledStatement,
            labelToken.ToFullString() + statement.ToFullString(),
            labelToken.FullSpan.Start,
            [labelToken])
    {
        LabelToken = labelToken;
        Statement = statement;
        _childNodes = [.. SingleNode(Statement)];
    }

    /// <summary>The label, including its leading colon.</summary>
    public ShellSyntaxToken LabelToken { get; }

    /// <summary>The labeled statement.</summary>
    public ShellStatementSyntax Statement { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    /// <summary>The label without its leading colon.</summary>
    public string Label => LabelToken.Text.TrimStart(':');

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitLabeledStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitLabeledStatement(this);
}
