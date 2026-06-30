namespace Meziantou.Framework.Assertions;

internal readonly struct NegativeRangeAssertionError<T>(string assertionName, T actualValue, T lowValue, T highValue, string? actualExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string AssertionName { get; } = assertionName;
    public string? ActualExpression { get; } = actualExpression;
    public T ActualValue { get; } = actualValue;
    public T LowValue { get; } = lowValue;
    public T HighValue { get; } = highValue;
}
