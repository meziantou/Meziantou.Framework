namespace Meziantou.Framework.Assertions;

internal readonly ref struct CollectionAllAssertionError<T>(CollectionSnapshot<T> actualValue, int index, Exception exception, string? actualExpression, string? assertionExpression)
{
    public string? ActualExpression { get; } = actualExpression;
    public string? AssertionExpression { get; } = assertionExpression;
    public CollectionSnapshot<T> ActualValue { get; } = actualValue;
    public int Index { get; } = index;
    public Exception Exception { get; } = exception;
}
