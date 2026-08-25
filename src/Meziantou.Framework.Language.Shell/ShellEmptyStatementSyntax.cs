namespace Meziantou.Framework.Language.Shell;

/// <summary>
/// Represents a statement with no content, produced by a separator that follows nothing. PowerShell allows this, as
/// in <c>;; Get-Date</c>. The node has no text of its own; only the separator that follows it does.
/// </summary>
public sealed class ShellEmptyStatementSyntax : ShellStatementSyntax
{
    public ShellEmptyStatementSyntax(int position = 0)
        : base(ShellSyntaxKind.EmptyStatement, string.Empty, position)
    {
    }

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitEmptyStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitEmptyStatement(this);
}
