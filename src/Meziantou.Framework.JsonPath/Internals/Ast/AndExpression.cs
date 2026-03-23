namespace Meziantou.Framework.Json.Internals;

internal sealed class AndExpression : LogicalExpression
{
    public AndExpression(LogicalExpression left, LogicalExpression right)
    {
        Left = left;
        Right = right;
    }

    public override LogicalExpressionKind Kind => LogicalExpressionKind.And;

    public LogicalExpression Left { get; }

    public LogicalExpression Right { get; }
}
