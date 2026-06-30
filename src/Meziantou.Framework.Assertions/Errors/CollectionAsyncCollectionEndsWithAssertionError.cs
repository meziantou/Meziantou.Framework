namespace Meziantou.Framework.Assertions;

internal readonly struct CollectionAsyncCollectionEndsWithAssertionError<TExpected, TActual>(CollectionSnapshot<TExpected> expectedValue, AsyncCollectionSnapshot<TActual> actualValue, int firstDifferenceIndex, string? actualExpression, string? expectedExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public CollectionSnapshot<TExpected> ExpectedValue { get; } = expectedValue;
    public AsyncCollectionSnapshot<TActual> ActualValue { get; } = actualValue;
    public int FirstDifferenceIndex { get; } = firstDifferenceIndex;
}
