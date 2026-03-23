namespace Meziantou.Framework.Json.Internals;

internal sealed class OrExpression : LogicalExpression
{
    public OrExpression(LogicalExpression left, LogicalExpression right)
    {
        Left = left;
        Right = right;
    }

    public override LogicalExpressionKind Kind => LogicalExpressionKind.Or;

    public LogicalExpression Left { get; }

    public LogicalExpression Right { get; }
}
