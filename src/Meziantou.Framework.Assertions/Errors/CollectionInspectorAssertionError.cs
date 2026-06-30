namespace Meziantou.Framework.Assertions;

internal readonly ref struct CollectionInspectorAssertionError<T>(CollectionSnapshot<T> actualValue, int index, Exception exception, string? actualExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string? ActualExpression { get; } = actualExpression;
    public CollectionSnapshot<T> ActualValue { get; } = actualValue;
    public int Index { get; } = index;
    public Exception Exception { get; } = exception;
}
