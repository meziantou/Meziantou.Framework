namespace Meziantou.Framework.Assertions;

internal readonly struct NullAssertionError(object? actualValue, string? actualExpression)
{
    public string? ActualExpression { get; } = actualExpression;
    public object? ActualValue { get; } = actualValue;
}
