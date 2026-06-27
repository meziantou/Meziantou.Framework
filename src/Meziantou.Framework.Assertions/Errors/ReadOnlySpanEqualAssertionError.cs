namespace Meziantou.Framework.Assertions;

internal readonly ref struct ReadOnlySpanEqualAssertionError<TExpected, TActual>(ReadOnlySpan<TExpected> expectedValue, ReadOnlySpan<TActual> actualValue, int firstDifferenceIndex, string? message, string? actualExpression, string? expectedExpression)
{
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public ReadOnlySpan<TExpected> ExpectedValue { get; } = expectedValue;
    public ReadOnlySpan<TActual> ActualValue { get; } = actualValue;
    public int FirstDifferenceIndex { get; } = firstDifferenceIndex;
}
