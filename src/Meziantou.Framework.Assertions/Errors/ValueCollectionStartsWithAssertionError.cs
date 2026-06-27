namespace Meziantou.Framework.Assertions;

internal readonly ref struct ValueCollectionStartsWithAssertionError<T>(T expectedValue, CollectionSnapshot<T> actualValue, string? actualExpression, string? expectedExpression)
{
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public T ExpectedValue { get; } = expectedValue;
    public CollectionSnapshot<T> ActualValue { get; } = actualValue;
}
