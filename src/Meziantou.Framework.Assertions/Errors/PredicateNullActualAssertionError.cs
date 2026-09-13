namespace Meziantou.Framework.Assertions;

internal readonly struct PredicateNullActualAssertionError(string assertionName, string? actualExpression, string? predicateExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string AssertionName { get; } = assertionName;
    public string? ActualExpression { get; } = actualExpression;
    public string? PredicateExpression { get; } = predicateExpression;
}
