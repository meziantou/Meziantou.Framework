namespace Meziantou.Framework.Assertions;

internal readonly ref struct ValueStartsWithAssertionError<T>(T expectedValue, ReadOnlySpan<T> actualValue, string? actualExpression, string? expectedExpression)
{
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public T ExpectedValue { get; } = expectedValue;
    public ReadOnlySpan<T> ActualValue { get; } = actualValue;
}
