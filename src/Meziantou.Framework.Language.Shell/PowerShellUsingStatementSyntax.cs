namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>using</c> statement, such as <c>using namespace System.IO</c>.</summary>
public sealed class PowerShellUsingStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellUsingStatementSyntax(
        ShellSyntaxToken usingKeyword,
        ShellSyntaxToken kindToken,
        ShellSyntaxNode target)
        : base(
            ShellSyntaxKind.PowerShellUsingStatement,
            usingKeyword.ToFullString() + kindToken.ToFullString() + target.ToFullString(),
            usingKeyword.FullSpan.Start,
            [usingKeyword, kindToken])
    {
        UsingKeyword = usingKeyword;
        KindToken = kindToken;
        Target = target;
        _childNodes = [.. SingleNode(Target)];
    }

    /// <summary>The <c>using</c> keyword.</summary>
    public ShellSyntaxToken UsingKeyword { get; }

    /// <summary>The <c>namespace</c>, <c>module</c>, or <c>assembly</c> keyword.</summary>
    public ShellSyntaxToken KindToken { get; }

    /// <summary>The namespace, module, or assembly being referenced.</summary>
    public ShellSyntaxNode Target { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitUsingStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitUsingStatement(this);
}
