namespace Meziantou.Framework.Assertions;

internal readonly struct ContainsPredicateNullActualAssertionError(string? actualExpression, string? predicateExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string? ActualExpression { get; } = actualExpression;
    public string? PredicateExpression { get; } = predicateExpression;
}
