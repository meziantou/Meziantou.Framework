namespace Meziantou.Framework.Assertions;

internal readonly ref struct CollectionEqualAssertionError<TExpected, TActual>(IEnumerable<TExpected> expectedValue, IEnumerable<TActual> actualValue, int firstDifferenceIndex, string? message, string? actualExpression, string? expectedExpression)
{
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public IEnumerable<TExpected> ExpectedValue { get; } = expectedValue;
    public IEnumerable<TActual> ActualValue { get; } = actualValue;
    public int FirstDifferenceIndex { get; } = firstDifferenceIndex;
}
