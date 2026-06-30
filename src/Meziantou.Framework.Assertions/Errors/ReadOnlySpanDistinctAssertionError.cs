namespace Meziantou.Framework.Assertions;

internal readonly ref struct ReadOnlySpanDistinctAssertionError<T>(ReadOnlySpan<T> actualValue, int duplicateIndex, int firstIndex, string? actualExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string? ActualExpression { get; } = actualExpression;
    public ReadOnlySpan<T> ActualValue { get; } = actualValue;
    public int DuplicateIndex { get; } = duplicateIndex;
    public int FirstIndex { get; } = firstIndex;
}
