namespace Meziantou.Framework.Json.Internals;

/// <summary>
/// A <c>logical-or-expr</c>. The operands of a chain are held flat rather than as nested pairs, so that
/// evaluating a chain of any length costs a single frame: the evaluator recurses per operand, and a nested
/// shape would let a query such as <c>$[?@ || @ || ...]</c> overflow the stack.
/// </summary>
internal sealed class OrExpression : LogicalExpression
{
    public OrExpression(LogicalExpression[] operands)
    {
        Operands = operands;
    }

    public override LogicalExpressionKind Kind => LogicalExpressionKind.Or;

    public LogicalExpression[] Operands { get; }
}
