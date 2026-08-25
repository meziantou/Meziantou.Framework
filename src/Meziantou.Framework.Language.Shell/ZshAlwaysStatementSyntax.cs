namespace Meziantou.Framework.Language.Shell;

/// <summary>
/// Represents the zsh <c>always</c> construct, <c>{ body } always { cleanup }</c>, whose second block runs whether or
/// not the first one failed.
/// </summary>
public sealed class ZshAlwaysStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public ZshAlwaysStatementSyntax(ShellStatementSyntax body, ShellSyntaxToken alwaysKeyword, ShellStatementSyntax alwaysBody)
        : base(
            ShellSyntaxKind.ZshAlwaysStatement,
            body.ToFullString() + alwaysKeyword.ToFullString() + alwaysBody.ToFullString(),
            body.FullSpan.Start,
            [alwaysKeyword])
    {
        Body = body;
        AlwaysKeyword = alwaysKeyword;
        AlwaysBody = alwaysBody;
        _childNodes = [body, alwaysBody];
    }

    /// <summary>The protected block.</summary>
    public ShellStatementSyntax Body { get; }

    public ShellSyntaxToken AlwaysKeyword { get; }

    /// <summary>The block that always runs afterwards.</summary>
    public ShellStatementSyntax AlwaysBody { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitZshAlwaysStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitZshAlwaysStatement(this);
}
