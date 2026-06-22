namespace Meziantou.Framework.Assertions;

internal readonly ref struct ReadOnlySpanEndsWithAssertionError<T>(ReadOnlySpan<T> expectedValue, ReadOnlySpan<T> actualValue, int firstDifferenceIndex, string? actualExpression, string? expectedExpression)
{
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public ReadOnlySpan<T> ExpectedValue { get; } = expectedValue;
    public ReadOnlySpan<T> ActualValue { get; } = actualValue;
    public int FirstDifferenceIndex { get; } = firstDifferenceIndex;
}
