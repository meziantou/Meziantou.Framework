namespace Meziantou.Framework.Assertions;

internal readonly struct StringContainsNullActualAssertionError(string expectedValue, StringComparison comparison, string? actualExpression, string? expectedExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public string ExpectedValue { get; } = expectedValue;
    public StringComparison Comparison { get; } = comparison;
}
