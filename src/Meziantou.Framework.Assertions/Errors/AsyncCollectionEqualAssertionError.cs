namespace Meziantou.Framework.Assertions;

internal readonly struct AsyncCollectionEqualAssertionError<TExpected, TActual>(AsyncCollectionSnapshot<TExpected> expectedValue, AsyncCollectionSnapshot<TActual> actualValue, int firstDifferenceIndex, string? message, string? actualExpression, string? expectedExpression)
{
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public AsyncCollectionSnapshot<TExpected> ExpectedValue { get; } = expectedValue;
    public AsyncCollectionSnapshot<TActual> ActualValue { get; } = actualValue;
    public int FirstDifferenceIndex { get; } = firstDifferenceIndex;
}
