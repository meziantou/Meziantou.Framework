namespace Meziantou.Framework.Assertions;

internal readonly ref struct CollectionSingleAssertionError<T>(CollectionSnapshot<T> actualValue, string? actualExpression)
{
    public string? ActualExpression { get; } = actualExpression;
    public CollectionSnapshot<T> ActualValue { get; } = actualValue;
}
