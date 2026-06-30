namespace Meziantou.Framework.Assertions;

internal readonly ref struct CollectionCountAssertionError<T>(string assertionName, string expectedCount, int actualCount, CollectionSnapshot<T> actualValue, string? actualExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string AssertionName { get; } = assertionName;
    public string ExpectedCount { get; } = expectedCount;
    public int ActualCount { get; } = actualCount;
    public string? ActualExpression { get; } = actualExpression;
    public CollectionSnapshot<T> ActualValue { get; } = actualValue;
}
