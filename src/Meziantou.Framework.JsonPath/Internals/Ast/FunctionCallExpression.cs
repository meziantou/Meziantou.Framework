namespace Meziantou.Framework.Json.Internals;

internal sealed class FunctionCallExpression : LogicalExpression
{
    public FunctionCallExpression(string name, FunctionArgument[] arguments, FunctionExpressionType resultType)
    {
        Name = name;
        Arguments = arguments;
        ResultType = resultType;
    }

    public override LogicalExpressionKind Kind => LogicalExpressionKind.FunctionCall;

    public string Name { get; }

    public FunctionArgument[] Arguments { get; }

    public FunctionExpressionType ResultType { get; }
}
