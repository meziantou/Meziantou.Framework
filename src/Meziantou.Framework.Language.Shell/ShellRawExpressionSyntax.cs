namespace Meziantou.Framework.Language.Shell;

/// <summary>
/// Represents expression text kept verbatim, without further structure. Used for constructs whose inner grammar the
/// tree does not model, and as the never-throw fallback when text cannot be parsed as an expression.
/// </summary>
public sealed class ShellRawExpressionSyntax : ShellExpressionSyntax
{
    public ShellRawExpressionSyntax(ShellSyntaxToken textToken)
        : base(ShellSyntaxKind.RawExpression, GetFullText(textToken), textToken?.FullSpan.Start ?? 0, [textToken!])
    {
        TextToken = textToken!;
    }

    public ShellSyntaxToken TextToken { get; }

    /// <summary>The raw expression text.</summary>
    public string Text => TextToken.Text;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitRawExpression(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitRawExpression(this);

    private static string GetFullText(ShellSyntaxToken textToken)
    {
        ArgumentNullException.ThrowIfNull(textToken);

        return textToken.ToFullString();
    }
}
