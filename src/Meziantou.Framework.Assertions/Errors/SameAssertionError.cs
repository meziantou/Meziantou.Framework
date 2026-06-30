namespace Meziantou.Framework.Assertions;

internal readonly ref struct SameAssertionError(object? expectedValue, object? actualValue, string? actualExpression, string? expectedExpression, string? message = null)
{
    public string? Message { get; } = message;
    public object? ExpectedValue { get; } = expectedValue;
    public object? ActualValue { get; } = actualValue;
    public string? ActualExpression { get; } = actualExpression;
    public string? ExpectedExpression { get; } = expectedExpression;
}
