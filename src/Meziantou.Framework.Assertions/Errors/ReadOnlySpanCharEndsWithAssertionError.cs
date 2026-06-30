namespace Meziantou.Framework.Assertions;

internal readonly ref struct ReadOnlySpanCharEndsWithAssertionError(ReadOnlySpan<char> expectedValue, ReadOnlySpan<char> actualValue, int firstDifferenceIndex, StringComparison comparison, string? actualExpression, string? expectedExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public ReadOnlySpan<char> ExpectedValue { get; } = expectedValue;
    public ReadOnlySpan<char> ActualValue { get; } = actualValue;
    public int FirstDifferenceIndex { get; } = firstDifferenceIndex;
    public StringComparison Comparison { get; } = comparison;
}
