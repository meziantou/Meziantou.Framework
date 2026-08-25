namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an arithmetic expansion, <c>$(( ... ))</c>.</summary>
public sealed class PosixArithmeticExpansionSyntax : ShellWordPartSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PosixArithmeticExpansionSyntax(ShellSyntaxToken openToken, ShellExpressionSyntax expression, ShellSyntaxToken closeToken)
        : base(
            ShellSyntaxKind.PosixArithmeticExpansion,
            BuildText(openToken, expression, closeToken),
            openToken?.FullSpan.Start ?? 0,
            [openToken!, closeToken!])
    {
        OpenToken = openToken!;
        Expression = expression!;
        _childNodes = [expression!];
        CloseToken = closeToken!;
    }

    /// <summary>The <c>$((</c> token.</summary>
    public ShellSyntaxToken OpenToken { get; }

    /// <summary>
    /// The arithmetic expression between the delimiters. Text the arithmetic grammar does not fit comes back as a
    /// <see cref="ShellRawExpressionSyntax"/> rather than being rejected.
    /// </summary>
    public ShellExpressionSyntax Expression { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    /// <summary>The <c>))</c> token.</summary>
    public ShellSyntaxToken CloseToken { get; }

    /// <summary>The expression text as written.</summary>
    public string ExpressionText => Expression.ToFullString();

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitArithmeticExpansion(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitArithmeticExpansion(this);

    private static string BuildText(ShellSyntaxToken openToken, ShellExpressionSyntax expression, ShellSyntaxToken closeToken)
    {
        ArgumentNullException.ThrowIfNull(openToken);
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(closeToken);

        return openToken.ToFullString() + expression.ToFullString() + closeToken.ToFullString();
    }
}
