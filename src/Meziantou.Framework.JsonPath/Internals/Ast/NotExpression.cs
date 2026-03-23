namespace Meziantou.Framework.Json.Internals;

internal sealed class NotExpression : LogicalExpression
{
    public NotExpression(LogicalExpression operand)
    {
        Operand = operand;
    }

    public override LogicalExpressionKind Kind => LogicalExpressionKind.Not;

    public LogicalExpression Operand { get; }
}
