namespace Meziantou.Framework.Assertions;

internal readonly ref struct CollectionDistinctAssertionError<T>(CollectionSnapshot<T> actualValue, int duplicateIndex, int firstIndex, string? actualExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string? ActualExpression { get; } = actualExpression;
    public CollectionSnapshot<T> ActualValue { get; } = actualValue;
    public int DuplicateIndex { get; } = duplicateIndex;
    public int FirstIndex { get; } = firstIndex;
}
