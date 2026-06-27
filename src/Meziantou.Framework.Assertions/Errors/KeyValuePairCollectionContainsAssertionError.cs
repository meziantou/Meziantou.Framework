namespace Meziantou.Framework.Assertions;

internal readonly ref struct KeyValuePairCollectionContainsAssertionError<TKey, TValue>(TKey expectedKey, CollectionSnapshot<KeyValuePair<TKey, TValue>> actualValue, string? actualExpression, string? expectedExpression)
{
    public string? ActualExpression { get; } = actualExpression;
    public string? ExpectedExpression { get; } = expectedExpression;
    public TKey ExpectedKey { get; } = expectedKey;
    public CollectionSnapshot<KeyValuePair<TKey, TValue>> ActualValue { get; } = actualValue;
}
