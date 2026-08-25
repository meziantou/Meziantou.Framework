namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an arithmetic expansion, <c>$(( ... ))</c>.</summary>
public sealed class PosixArithmeticExpansionSyntax : ShellWordPartSyntax
{
    public PosixArithmeticExpansionSyntax(ShellSyntaxToken openToken, ShellSyntaxToken expressionToken, ShellSyntaxToken closeToken)
        : base(
            ShellSyntaxKind.PosixArithmeticExpansion,
            BuildText(openToken, expressionToken, closeToken),
            openToken?.FullSpan.Start ?? 0,
            [openToken!, expressionToken!, closeToken!])
    {
        OpenToken = openToken!;
        ExpressionToken = expressionToken!;
        CloseToken = closeToken!;
    }

    /// <summary>The <c>$((</c> token.</summary>
    public ShellSyntaxToken OpenToken { get; }

    /// <summary>The raw arithmetic expression text between the delimiters.</summary>
    public ShellSyntaxToken ExpressionToken { get; }

    /// <summary>The <c>))</c> token.</summary>
    public ShellSyntaxToken CloseToken { get; }

    public string Expression => ExpressionToken.Text;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitArithmeticExpansion(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitArithmeticExpansion(this);

    private static string BuildText(ShellSyntaxToken openToken, ShellSyntaxToken expressionToken, ShellSyntaxToken closeToken)
    {
        ArgumentNullException.ThrowIfNull(openToken);
        ArgumentNullException.ThrowIfNull(expressionToken);
        ArgumentNullException.ThrowIfNull(closeToken);

        return openToken.ToFullString() + expressionToken.ToFullString() + closeToken.ToFullString();
    }
}
