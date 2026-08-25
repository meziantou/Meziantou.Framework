namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents member access, <c>$x.Name</c> or <c>[Type]::Member</c>.</summary>
public sealed class PowerShellMemberAccessExpressionSyntax : ShellExpressionSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellMemberAccessExpressionSyntax(
        ShellExpressionSyntax target,
        ShellSyntaxToken operatorToken,
        ShellSyntaxToken memberNameToken)
        : base(
            ShellSyntaxKind.PowerShellMemberAccess,
            target.ToFullString() + operatorToken.ToFullString() + memberNameToken.ToFullString(),
            target.FullSpan.Start,
            [operatorToken, memberNameToken])
    {
        Target = target;
        OperatorToken = operatorToken;
        MemberNameToken = memberNameToken;
        _childNodes = [.. SingleNode(Target)];
    }

    /// <summary>The expression the member is read from.</summary>
    public ShellExpressionSyntax Target { get; }

    /// <summary>The <c>.</c> or <c>::</c> token.</summary>
    public ShellSyntaxToken OperatorToken { get; }

    /// <summary>The member name.</summary>
    public ShellSyntaxToken MemberNameToken { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    /// <summary>Returns <see langword="true"/> for the static member operator, <c>::</c>.</summary>
    public bool IsStatic => OperatorToken.Kind == ShellSyntaxKind.ColonColonToken;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitMemberAccess(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitMemberAccess(this);
}
