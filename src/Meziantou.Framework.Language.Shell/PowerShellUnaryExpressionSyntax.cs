namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a prefix or postfix unary expression such as <c>-not $x</c> or <c>$i++</c>.</summary>
public sealed class PowerShellUnaryExpressionSyntax : ShellExpressionSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellUnaryExpressionSyntax(
        ShellSyntaxKind kind,
        ShellSyntaxToken? prefixOperatorToken,
        ShellExpressionSyntax operand,
        ShellSyntaxToken? postfixOperatorToken)
        : base(
            kind,
            (prefixOperatorToken?.ToFullString() ?? string.Empty) + operand.ToFullString() + (postfixOperatorToken?.ToFullString() ?? string.Empty),
            operand.FullSpan.Start,
            BuildTokens(prefixOperatorToken, postfixOperatorToken))
    {
        PrefixOperatorToken = prefixOperatorToken;
        Operand = operand;
        PostfixOperatorToken = postfixOperatorToken;
        _childNodes = [.. SingleNode(Operand)];
    }

    /// <summary>The operator when it precedes the operand.</summary>
    public ShellSyntaxToken? PrefixOperatorToken { get; }

    /// <summary>The operand.</summary>
    public ShellExpressionSyntax Operand { get; }

    /// <summary>The operator when it follows the operand.</summary>
    public ShellSyntaxToken? PostfixOperatorToken { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitUnaryExpression(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitUnaryExpression(this);

    private static List<ShellSyntaxToken> BuildTokens(
        ShellSyntaxToken? prefixOperatorToken,
        ShellSyntaxToken? postfixOperatorToken)
    {
        var tokens = new List<ShellSyntaxToken>();
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
