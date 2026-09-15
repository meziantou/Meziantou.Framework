namespace Meziantou.Framework.Assertions;

internal readonly ref struct ReadOnlySpanDoesNotContainItemAssertionError<T>(T expectedValue, ReadOnlySpan<T> actualValue, int foundIndex, string? actualExpression, string? expectedExpression, string? message)
{
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public T ExpectedValue { get; } = expectedValue;
    public ReadOnlySpan<T> ActualValue { get; } = actualValue;
    public int FoundIndex { get; } = foundIndex;
}
