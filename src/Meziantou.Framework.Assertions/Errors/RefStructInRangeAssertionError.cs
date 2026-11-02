namespace Meziantou.Framework.Assertions;

internal readonly ref struct RefStructInRangeAssertionError(bool isNegative, Type valueType, string? actualExpression, string? lowExpression, string? highExpression, string? message)
{
    public bool IsNegative { get; } = isNegative;
    public string? Message { get; } = message;
    public Type ValueType { get; } = valueType;
    public string? ActualExpression { get; } = actualExpression;
    public string? LowExpression { get; } = lowExpression;
    public string? HighExpression { get; } = highExpression;
}
