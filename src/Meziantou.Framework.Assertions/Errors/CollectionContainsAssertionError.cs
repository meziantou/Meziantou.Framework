namespace Meziantou.Framework.Assertions;

internal readonly ref struct CollectionContainsAssertionError<TExpected, TActual>(CollectionSnapshot<TExpected> expectedValue, CollectionSnapshot<TActual> actualValue, string? actualExpression, string? expectedExpression)
{
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public CollectionSnapshot<TExpected> ExpectedValue { get; } = expectedValue;
    public CollectionSnapshot<TActual> ActualValue { get; } = actualValue;
}
