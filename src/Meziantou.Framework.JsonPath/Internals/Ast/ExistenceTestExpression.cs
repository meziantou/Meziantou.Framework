namespace Meziantou.Framework.Json.Internals;

internal sealed class ExistenceTestExpression : LogicalExpression
{
    public ExistenceTestExpression(FilterQuery query)
    {
        Query = query;
    }

    public override LogicalExpressionKind Kind => LogicalExpressionKind.ExistenceTest;

    public FilterQuery Query { get; }
}
