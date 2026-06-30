namespace Meziantou.Framework.Assertions;

internal readonly ref struct CollectionContainsPredicateAssertionError<T>(CollectionSnapshot<T> matchingValues, string? actualExpression, string? predicateExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string? ActualExpression { get; } = actualExpression;
    public string? PredicateExpression { get; } = predicateExpression;
    public CollectionSnapshot<T> MatchingValues { get; } = matchingValues;
}
