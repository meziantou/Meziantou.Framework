namespace Meziantou.Framework.Assertions;

internal readonly ref struct SameAssertionError(object? expectedValue, object? actualValue, string? actualExpression, string? expectedExpression)
{
    public object? ExpectedValue { get; } = expectedValue;
    public object? ActualValue { get; } = actualValue;
    public string? ActualExpression { get; } = actualExpression;
    public string? ExpectedExpression { get; } = expectedExpression;
}
