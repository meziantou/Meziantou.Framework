namespace Meziantou.Framework.Assertions;

internal readonly struct AsyncCollectionEmptyAssertionError<T>(AsyncCollectionSnapshot<T> actualValue, string? actualExpression)
{
    public string? ActualExpression { get; } = actualExpression;
    public AsyncCollectionSnapshot<T> ActualValue { get; } = actualValue;
}
