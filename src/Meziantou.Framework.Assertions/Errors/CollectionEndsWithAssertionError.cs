namespace Meziantou.Framework.Assertions;

internal readonly ref struct CollectionEndsWithAssertionError<TExpected, TActual>(CollectionSnapshot<TExpected> expectedValue, CollectionSnapshot<TActual> actualValue, int firstDifferenceIndex, string? actualExpression, string? expectedExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public CollectionSnapshot<TExpected> ExpectedValue { get; } = expectedValue;
    public CollectionSnapshot<TActual> ActualValue { get; } = actualValue;
    public int FirstDifferenceIndex { get; } = firstDifferenceIndex;
}
