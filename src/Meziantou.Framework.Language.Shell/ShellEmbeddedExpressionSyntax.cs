namespace Meziantou.Framework.Language.Shell;

/// <summary>
/// Wraps an expression used as part of a command word, such as <c>(Get-Date)</c> or <c>@{ a = 1 }</c> passed as an
/// argument. It lets a word hold structured expressions alongside plain literal text.
/// </summary>
public sealed class ShellEmbeddedExpressionSyntax : ShellWordPartSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public ShellEmbeddedExpressionSyntax(ShellSyntaxNode expression)
        : base(ShellSyntaxKind.EmbeddedExpression, GetFullText(expression), expression?.FullSpan.Start ?? 0)
    {
        Expression = expression!;
        _childNodes = [expression!];
    }

    public ShellSyntaxNode Expression { get; }
    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitEmbeddedExpression(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitEmbeddedExpression(this);

    private static string GetFullText(ShellSyntaxNode expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        return expression.ToFullString();
    }
}
