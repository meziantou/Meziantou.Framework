namespace Meziantou.Framework.Assertions;

internal readonly ref struct CollectionSinglePredicateAssertionError<T>(CollectionSnapshot<T> matchingValues, string? actualExpression, string? predicateExpression)
{
    public string? ActualExpression { get; } = actualExpression;
    public string? PredicateExpression { get; } = predicateExpression;
    public CollectionSnapshot<T> MatchingValues { get; } = matchingValues;
}
