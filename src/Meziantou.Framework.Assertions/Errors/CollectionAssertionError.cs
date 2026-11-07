namespace Meziantou.Framework.Assertions;

internal readonly ref struct CollectionAssertionError<T>(CollectionSnapshot<T> actualValue, int expectedCount, string? actualExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string? ActualExpression { get; } = actualExpression;
    public CollectionSnapshot<T> ActualValue { get; } = actualValue;
    public int ExpectedCount { get; } = expectedCount;

    /// <summary>Gets the number of items, read when the message is formatted so the items observed for it count too.</summary>
    public string ActualCount => ActualValue.GetCountText();
}
