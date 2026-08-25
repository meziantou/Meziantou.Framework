namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>finally</c> clause.</summary>
public sealed class PowerShellFinallyClauseSyntax : ShellSyntaxNode
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellFinallyClauseSyntax(
        ShellSyntaxToken finallyKeyword,
        PowerShellScriptBlockSyntax body)
        : base(
            ShellSyntaxKind.PowerShellFinallyClause,
            finallyKeyword.ToFullString() + body.ToFullString(),
            finallyKeyword.FullSpan.Start,
            [finallyKeyword])
    {
        FinallyKeyword = finallyKeyword;
        Body = body;
        _childNodes = [.. SingleNode(Body)];
    }

    /// <summary>The <c>finally</c> keyword.</summary>
    public ShellSyntaxToken FinallyKeyword { get; }

    /// <summary>The clause body.</summary>
    public PowerShellScriptBlockSyntax Body { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitFinallyClause(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitFinallyClause(this);
}
