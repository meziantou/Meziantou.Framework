namespace Meziantou.Framework.Json.Internals;

internal sealed class ComparisonExpression : LogicalExpression
{
    public ComparisonExpression(Comparable left, ComparisonOperator op, Comparable right)
    {
        Left = left;
        Operator = op;
        Right = right;
    }

    public override LogicalExpressionKind Kind => LogicalExpressionKind.Comparison;

    public Comparable Left { get; }

    public ComparisonOperator Operator { get; }

    public Comparable Right { get; }
}
