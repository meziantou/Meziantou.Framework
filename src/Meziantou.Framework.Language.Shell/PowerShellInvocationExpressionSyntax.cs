namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a method call, <c>$x.Method(1, 2)</c>.</summary>
public sealed class PowerShellInvocationExpressionSyntax : ShellExpressionSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellInvocationExpressionSyntax(
        ShellExpressionSyntax target,
        ShellSyntaxToken openParenToken,
        IReadOnlyList<ShellExpressionSyntax>? arguments,
        IReadOnlyList<ShellSyntaxToken>? separatorTokens,
        ShellSyntaxToken closeParenToken)
        : base(
            ShellSyntaxKind.PowerShellInvocation,
            target.ToFullString() + openParenToken.ToFullString() + SeparatedNodes.BuildText(arguments, separatorTokens) + closeParenToken.ToFullString(),
            target.FullSpan.Start,
            BuildTokens(openParenToken, separatorTokens, closeParenToken))
    {
        Target = target;
        OpenParenToken = openParenToken;
        Arguments = arguments ?? [];
        SeparatorTokens = separatorTokens ?? [];
        CloseParenToken = closeParenToken;
        _childNodes = [.. SingleNode(Target), .. (Arguments as IEnumerable<ShellSyntaxNode>)];
    }

    /// <summary>The member being invoked.</summary>
    public ShellExpressionSyntax Target { get; }

    /// <summary>The opening parenthesis.</summary>
    public ShellSyntaxToken OpenParenToken { get; }

    /// <summary>The call arguments.</summary>
    public IReadOnlyList<ShellExpressionSyntax> Arguments { get; }

    /// <summary>The separator that follows each entry of <see cref="Arguments"/>.</summary>
    public IReadOnlyList<ShellSyntaxToken> SeparatorTokens { get; }

    /// <summary>The closing parenthesis.</summary>
    public ShellSyntaxToken CloseParenToken { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitInvocation(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitInvocation(this);

    private static List<ShellSyntaxToken> BuildTokens(
        ShellSyntaxToken openParenToken,
        IReadOnlyList<ShellSyntaxToken>? separatorTokens,
        ShellSyntaxToken closeParenToken)
    {
        var tokens = new List<ShellSyntaxToken>();
        tokens.Add(openParenToken);
        tokens.AddRange(separatorTokens ?? []);
        tokens.Add(closeParenToken);

        return tokens;
    }
}
