namespace Meziantou.Framework.Assertions;

internal readonly ref struct IsAssignableToAssertionError(Type expectedType, object? actualValue, string? actualExpression, string? message = null)
{
    public string? Message { get; } = message;
    public Type ExpectedType { get; } = expectedType;
    public object? ActualValue { get; } = actualValue;
    public string? ActualExpression { get; } = actualExpression;
}
