namespace Meziantou.Framework.Json.Internals;

/// <summary>Represents an argument passed to a function call in a filter expression.</summary>
internal sealed class FunctionArgument
{
    private FunctionArgument(FunctionArgumentKind kind, object? value)
    {
        Kind = kind;
        Value = value;
        if (kind is FunctionArgumentKind.Literal)
        {
            IsScalar = ScalarValue.TryFromLiteral(value, out var scalar);
            Scalar = scalar;
        }
    }

    public FunctionArgumentKind Kind { get; }

    /// <summary>
    /// The argument value. Type depends on <see cref="Kind"/>:
    /// <list type="bullet">
    /// <item><see cref="FunctionArgumentKind.Literal"/>: an <see cref="object"/> (null, bool, string, or <see cref="NumberLiteral"/>)</item>
    /// <item><see cref="FunctionArgumentKind.FilterQuery"/>: a <see cref="FilterQuery"/></item>
    /// <item><see cref="FunctionArgumentKind.LogicalExpression"/>: a <see cref="LogicalExpression"/></item>
    /// <item><see cref="FunctionArgumentKind.FunctionCall"/>: a <see cref="FunctionCallExpression"/></item>
    /// </list>
    /// </summary>
    public object? Value { get; }

    /// <summary>Gets whether the argument is a literal, which <see cref="Scalar"/> holds.</summary>
    public bool IsScalar { get; }

    /// <summary>Gets the value of a literal argument, converted once rather than for each node a filter tests.</summary>
    public ScalarValue Scalar { get; }

    public static FunctionArgument FromLiteral(object? value) => new(FunctionArgumentKind.Literal, value);

    public static FunctionArgument FromFilterQuery(FilterQuery query) => new(FunctionArgumentKind.FilterQuery, query);

    public static FunctionArgument FromLogicalExpression(LogicalExpression expr) => new(FunctionArgumentKind.LogicalExpression, expr);

    public static FunctionArgument FromFunctionCall(FunctionCallExpression func) => new(FunctionArgumentKind.FunctionCall, func);
}
