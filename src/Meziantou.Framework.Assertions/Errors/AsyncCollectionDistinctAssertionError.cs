namespace Meziantou.Framework.Assertions;

internal readonly struct AsyncCollectionDistinctAssertionError<T>(AsyncCollectionSnapshot<T> actualValue, int duplicateIndex, int firstIndex, string? actualExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string? ActualExpression { get; } = actualExpression;
    public AsyncCollectionSnapshot<T> ActualValue { get; } = actualValue;
    public int DuplicateIndex { get; } = duplicateIndex;
    public int FirstIndex { get; } = firstIndex;
}
