namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>call</c> statement, invoking a label or another script.</summary>
public sealed class CmdCallStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public CmdCallStatementSyntax(
        ShellSyntaxToken callKeyword,
        ShellStatementSyntax target)
        : base(
            ShellSyntaxKind.CmdCallStatement,
            callKeyword.ToFullString() + target.ToFullString(),
            callKeyword.FullSpan.Start,
            [callKeyword])
    {
        CallKeyword = callKeyword;
        Target = target;
        _childNodes = [.. SingleNode(Target)];
    }

    /// <summary>The <c>call</c> keyword.</summary>
    public ShellSyntaxToken CallKeyword { get; }

    /// <summary>The invoked command.</summary>
    public ShellStatementSyntax Target { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitCmdCall(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitCmdCall(this);
}
