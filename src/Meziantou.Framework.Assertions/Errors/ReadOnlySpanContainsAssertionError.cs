namespace Meziantou.Framework.Assertions;

internal readonly ref struct ReadOnlySpanContainsAssertionError<T>(ReadOnlySpan<T> expectedValue, ReadOnlySpan<T> actualValue, string? actualExpression, string? expectedExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public ReadOnlySpan<T> ExpectedValue { get; } = expectedValue;
    public ReadOnlySpan<T> ActualValue { get; } = actualValue;
}
