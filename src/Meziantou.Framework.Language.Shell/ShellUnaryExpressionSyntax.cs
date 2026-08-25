namespace Meziantou.Framework.Language.Shell;

/// <summary>
/// Represents a prefix expression such as <c>-x</c>, <c>!$a</c>, or the conditional test <c>-f file</c>, and the
/// postfix increment forms <c>i++</c> and <c>i--</c>.
/// </summary>
public sealed class ShellUnaryExpressionSyntax : ShellExpressionSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public ShellUnaryExpressionSyntax(ShellSyntaxKind kind, ShellSyntaxToken? prefixOperatorToken, ShellExpressionSyntax operand, ShellSyntaxToken? postfixOperatorToken)
        : base(
            kind,
            (prefixOperatorToken?.ToFullString() ?? string.Empty) + operand.ToFullString() + (postfixOperatorToken?.ToFullString() ?? string.Empty),
            (prefixOperatorToken ?? operand.DescendantTokens().FirstOrDefault())?.FullSpan.Start ?? operand.FullSpan.Start,
            BuildTokens(prefixOperatorToken, postfixOperatorToken))
    {
        PrefixOperatorToken = prefixOperatorToken;
        Operand = operand;
        PostfixOperatorToken = postfixOperatorToken;
        _childNodes = [operand];
    }

    public ShellSyntaxToken? PrefixOperatorToken { get; }
    public ShellExpressionSyntax Operand { get; }
    public ShellSyntaxToken? PostfixOperatorToken { get; }

    /// <summary>The operator text, whichever side it is on.</summary>
    public string OperatorText => (PrefixOperatorToken ?? PostfixOperatorToken)?.Text ?? string.Empty;

    /// <summary>Returns <see langword="true"/> when the operator follows the operand, as in <c>i++</c>.</summary>
    public bool IsPostfix => Kind == ShellSyntaxKind.PostfixUnaryExpression;

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitShellUnaryExpression(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitShellUnaryExpression(this);

    private static List<ShellSyntaxToken> BuildTokens(ShellSyntaxToken? prefixOperatorToken, ShellSyntaxToken? postfixOperatorToken)
    {
        var tokens = new List<ShellSyntaxToken>(2);
        if (prefixOperatorToken is not null)
        {
            tokens.Add(prefixOperatorToken);
        }

        if (postfixOperatorToken is not null)
        {
            tokens.Add(postfixOperatorToken);
        }

        return tokens;
    }
}
