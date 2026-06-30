namespace Meziantou.Framework.Assertions;

internal readonly struct AsyncCollectionStartsWithAssertionError<T>(AsyncCollectionSnapshot<T> expectedValue, AsyncCollectionSnapshot<T> actualValue, int firstDifferenceIndex, string? actualExpression, string? expectedExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public AsyncCollectionSnapshot<T> ExpectedValue { get; } = expectedValue;
    public AsyncCollectionSnapshot<T> ActualValue { get; } = actualValue;
    public int FirstDifferenceIndex { get; } = firstDifferenceIndex;
}
