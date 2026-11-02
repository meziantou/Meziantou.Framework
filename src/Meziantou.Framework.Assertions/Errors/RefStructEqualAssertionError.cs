namespace Meziantou.Framework.Assertions;

internal readonly ref struct RefStructEqualAssertionError(bool isNegative, Type valueType, string? message, string? actualExpression, string? expectedExpression)
{
    public bool IsNegative { get; } = isNegative;
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public Type ValueType { get; } = valueType;
}
