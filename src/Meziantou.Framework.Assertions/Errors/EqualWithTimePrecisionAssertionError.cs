namespace Meziantou.Framework.Assertions;

internal readonly struct EqualWithTimePrecisionAssertionError<T>(bool isNegative, T expectedValue, T actualValue, TimeSpan difference, TimeSpan precision, string? message, string? actualExpression, string? expectedExpression)
{
    public bool IsNegative { get; } = isNegative;
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public T ExpectedValue { get; } = expectedValue;
    public T ActualValue { get; } = actualValue;
    public TimeSpan Difference { get; } = difference;
    public TimeSpan Precision { get; } = precision;
}
