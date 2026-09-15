namespace Meziantou.Framework.Assertions;

internal readonly ref struct CollectionEqualAssertionError<TExpected, TActual>(CollectionSnapshot<TExpected> expectedValue, CollectionSnapshot<TActual> actualValue, int firstDifferenceIndex, string? message, string? actualExpression, string? expectedExpression)
{
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public CollectionSnapshot<TExpected> ExpectedValue { get; } = expectedValue;
    public CollectionSnapshot<TActual> ActualValue { get; } = actualValue;
    public int FirstDifferenceIndex { get; } = firstDifferenceIndex;
}
