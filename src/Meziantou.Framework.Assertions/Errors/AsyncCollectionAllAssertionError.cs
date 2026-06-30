namespace Meziantou.Framework.Assertions;

internal readonly struct AsyncCollectionAllAssertionError<T>(AsyncCollectionSnapshot<T> actualValue, int index, Exception exception, string? actualExpression, string? assertionExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string? ActualExpression { get; } = actualExpression;
    public string? AssertionExpression { get; } = assertionExpression;
    public AsyncCollectionSnapshot<T> ActualValue { get; } = actualValue;
    public int Index { get; } = index;
    public Exception Exception { get; } = exception;
}
