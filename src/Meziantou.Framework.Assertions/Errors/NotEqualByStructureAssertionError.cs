namespace Meziantou.Framework.Assertions;

internal readonly struct NotEqualByStructureAssertionError(object? expectedValue, object? actualValue, string? actualExpression, string? expectedExpression, string? message)
{
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public object? ExpectedValue { get; } = expectedValue;
    public object? ActualValue { get; } = actualValue;
}
