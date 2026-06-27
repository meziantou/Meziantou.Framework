namespace Meziantou.Framework.Assertions;

internal readonly struct ContainsPredicateNullActualAssertionError(string? actualExpression, string? predicateExpression)
{
    public string? ActualExpression { get; } = actualExpression;
    public string? PredicateExpression { get; } = predicateExpression;
}
