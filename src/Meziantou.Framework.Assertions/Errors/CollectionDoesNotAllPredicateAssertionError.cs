namespace Meziantou.Framework.Assertions;

internal readonly ref struct CollectionDoesNotAllPredicateAssertionError<T>(CollectionSnapshot<T> actualValue, string? actualExpression, string? predicateExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string? ActualExpression { get; } = actualExpression;
    public string? PredicateExpression { get; } = predicateExpression;
    public CollectionSnapshot<T> ActualValue { get; } = actualValue;
}
