namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents the <c>else</c> clause of a <see cref="CmdIfStatementSyntax"/>.</summary>
public sealed class CmdElseClauseSyntax : ShellSyntaxNode
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public CmdElseClauseSyntax(
        ShellSyntaxToken elseKeyword,
        ShellStatementSyntax body)
        : base(
            ShellSyntaxKind.CmdElseClause,
            elseKeyword.ToFullString() + body.ToFullString(),
            elseKeyword.FullSpan.Start,
            [elseKeyword])
    {
        ElseKeyword = elseKeyword;
        Body = body;
        _childNodes = [.. SingleNode(Body)];
    }

    /// <summary>The <c>else</c> keyword.</summary>
    public ShellSyntaxToken ElseKeyword { get; }

    /// <summary>The statement run when the condition fails.</summary>
    public ShellStatementSyntax Body { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitCmdElseClause(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitCmdElseClause(this);
}
