namespace Meziantou.Framework.Assertions;

internal readonly ref struct IsAssignableToAssertionError(Type expectedType, object? actualValue, string? actualExpression)
{
    public Type ExpectedType { get; } = expectedType;
    public object? ActualValue { get; } = actualValue;
    public string? ActualExpression { get; } = actualExpression;
}
