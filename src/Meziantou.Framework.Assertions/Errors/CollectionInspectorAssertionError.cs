namespace Meziantou.Framework.Assertions;

internal readonly ref struct CollectionInspectorAssertionError<T>(CollectionSnapshot<T> actualValue, int index, Exception exception, string? actualExpression)
{
    public string? ActualExpression { get; } = actualExpression;
    public CollectionSnapshot<T> ActualValue { get; } = actualValue;
    public int Index { get; } = index;
    public Exception Exception { get; } = exception;
}
