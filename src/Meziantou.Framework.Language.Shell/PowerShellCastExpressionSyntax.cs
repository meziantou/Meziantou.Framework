namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a cast expression, <c>[int]$value</c>.</summary>
public sealed class PowerShellCastExpressionSyntax : ShellExpressionSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellCastExpressionSyntax(PowerShellTypeLiteralSyntax type, ShellExpressionSyntax operand)
        : base(
            ShellSyntaxKind.PowerShellCastExpression,
            GetFullText(type, operand),
            type?.FullSpan.Start ?? 0)
    {
        Type = type!;
        Operand = operand!;
        _childNodes = [type!, operand!];
    }

    /// <summary>The target type.</summary>
    public PowerShellTypeLiteralSyntax Type { get; }

    /// <summary>The value being cast.</summary>
    public ShellExpressionSyntax Operand { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitCastExpression(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitCastExpression(this);

    private static string GetFullText(PowerShellTypeLiteralSyntax type, ShellExpressionSyntax operand)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(operand);

        return type.ToFullString() + operand.ToFullString();
    }
}
