namespace Meziantou.Framework.Assertions;

internal readonly ref struct ValueCollectionEndsWithAssertionError<T>(T expectedValue, CollectionSnapshot<T> actualValue, string? actualExpression, string? expectedExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public T ExpectedValue { get; } = expectedValue;
    public CollectionSnapshot<T> ActualValue { get; } = actualValue;
}
