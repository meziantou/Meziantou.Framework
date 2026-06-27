namespace Meziantou.Framework.Assertions;

internal readonly ref struct ReadOnlySpanCharContainsAssertionError(ReadOnlySpan<char> expectedValue, ReadOnlySpan<char> actualValue, StringComparison comparison, string? actualExpression, string? expectedExpression)
{
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public ReadOnlySpan<char> ExpectedValue { get; } = expectedValue;
    public ReadOnlySpan<char> ActualValue { get; } = actualValue;
    public StringComparison Comparison { get; } = comparison;
}
