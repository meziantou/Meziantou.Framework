namespace Meziantou.Framework.Assertions;

internal readonly ref struct ReadOnlySpanStartsWithAssertionError<T>(ReadOnlySpan<T> expectedValue, ReadOnlySpan<T> actualValue, int firstDifferenceIndex, string? actualExpression, string? expectedExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public ReadOnlySpan<T> ExpectedValue { get; } = expectedValue;
    public ReadOnlySpan<T> ActualValue { get; } = actualValue;
    public int FirstDifferenceIndex { get; } = firstDifferenceIndex;
}
