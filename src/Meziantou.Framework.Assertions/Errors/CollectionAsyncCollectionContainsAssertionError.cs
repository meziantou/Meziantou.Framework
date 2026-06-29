namespace Meziantou.Framework.Assertions;

internal readonly struct CollectionAsyncCollectionContainsAssertionError<TExpected, TActual>(CollectionSnapshot<TExpected> expectedValue, AsyncCollectionSnapshot<TActual> actualValue, string? actualExpression, string? expectedExpression)
{
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public CollectionSnapshot<TExpected> ExpectedValue { get; } = expectedValue;
    public AsyncCollectionSnapshot<TActual> ActualValue { get; } = actualValue;
}
