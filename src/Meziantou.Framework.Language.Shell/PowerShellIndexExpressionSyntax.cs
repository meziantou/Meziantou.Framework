namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an index access, <c>$x[0]</c>.</summary>
public sealed class PowerShellIndexExpressionSyntax : ShellExpressionSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellIndexExpressionSyntax(
        ShellExpressionSyntax target,
        ShellSyntaxToken openBracketToken,
        ShellSyntaxNode index,
        ShellSyntaxToken closeBracketToken)
        : base(
            ShellSyntaxKind.PowerShellIndex,
            target.ToFullString() + openBracketToken.ToFullString() + index.ToFullString() + closeBracketToken.ToFullString(),
            target.FullSpan.Start,
            [openBracketToken, closeBracketToken])
    {
        Target = target;
        OpenBracketToken = openBracketToken;
        Index = index;
        CloseBracketToken = closeBracketToken;
        _childNodes = [.. SingleNode(Target), .. SingleNode(Index)];
    }

    /// <summary>The indexed expression.</summary>
    public ShellExpressionSyntax Target { get; }

    /// <summary>The opening bracket.</summary>
    public ShellSyntaxToken OpenBracketToken { get; }

    /// <summary>The index expression.</summary>
    public ShellSyntaxNode Index { get; }

    /// <summary>The closing bracket.</summary>
    public ShellSyntaxToken CloseBracketToken { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitIndexExpression(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitIndexExpression(this);
}
