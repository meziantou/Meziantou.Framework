namespace Meziantou.Framework.Assertions;

internal readonly ref struct CollectionDoesNotAllPredicateAssertionError<T>(CollectionSnapshot<T> actualValue, string? actualExpression, string? predicateExpression)
{
    public string? ActualExpression { get; } = actualExpression;
    public string? PredicateExpression { get; } = predicateExpression;
    public CollectionSnapshot<T> ActualValue { get; } = actualValue;
}
