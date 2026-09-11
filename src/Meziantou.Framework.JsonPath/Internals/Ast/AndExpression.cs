namespace Meziantou.Framework.Json.Internals;

/// <summary>
/// A <c>logical-and-expr</c>. The operands of a chain are held flat rather than as nested pairs, for the reason
/// given on <see cref="OrExpression"/>.
/// </summary>
internal sealed class AndExpression : LogicalExpression
{
    public AndExpression(LogicalExpression[] operands)
    {
        Operands = operands;
    }

    public override LogicalExpressionKind Kind => LogicalExpressionKind.And;

    public LogicalExpression[] Operands { get; }
}
