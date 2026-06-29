namespace Meziantou.Framework.Assertions;

internal readonly ref struct CollectionSetAssertionError<T>(CollectionSnapshot<T> expectedValue, CollectionSnapshot<T> actualValue, bool isSuperset, string? actualExpression, string? expectedExpression)
{
    public CollectionSnapshot<T> ExpectedValue { get; } = expectedValue;
    public CollectionSnapshot<T> ActualValue { get; } = actualValue;
    public bool IsSuperset { get; } = isSuperset;
    public string? ActualExpression { get; } = actualExpression;
    public string? ExpectedExpression { get; } = expectedExpression;
}
