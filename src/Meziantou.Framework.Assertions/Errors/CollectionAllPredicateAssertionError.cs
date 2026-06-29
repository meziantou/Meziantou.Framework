namespace Meziantou.Framework.Assertions;

internal readonly ref struct CollectionAllPredicateAssertionError<T>(CollectionSnapshot<T> actualValue, int index, string? actualExpression, string? predicateExpression)
{
    public string? ActualExpression { get; } = actualExpression;
    public string? PredicateExpression { get; } = predicateExpression;
    public CollectionSnapshot<T> ActualValue { get; } = actualValue;
    public int Index { get; } = index;
}
