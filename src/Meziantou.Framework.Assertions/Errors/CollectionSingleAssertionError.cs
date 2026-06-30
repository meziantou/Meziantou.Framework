namespace Meziantou.Framework.Assertions;

internal readonly ref struct CollectionSingleAssertionError<T>(CollectionSnapshot<T> actualValue, string? actualExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string? ActualExpression { get; } = actualExpression;
    public CollectionSnapshot<T> ActualValue { get; } = actualValue;
}
