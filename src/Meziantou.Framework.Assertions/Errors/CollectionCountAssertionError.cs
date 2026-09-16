namespace Meziantou.Framework.Assertions;

internal readonly ref struct CollectionCountAssertionError<T>(string assertionName, string expectedCount, CollectionSnapshot<T> actualValue, string? actualExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string AssertionName { get; } = assertionName;
    public string ExpectedCount { get; } = expectedCount;

    /// <summary>Gets the number of items, read when the message is formatted so the items observed for it count too.</summary>
    public string ActualCount => ActualValue.GetCountText();
    public string? ActualExpression { get; } = actualExpression;
    public CollectionSnapshot<T> ActualValue { get; } = actualValue;
}
