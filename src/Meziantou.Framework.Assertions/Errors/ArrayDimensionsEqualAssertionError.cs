namespace Meziantou.Framework.Assertions;

internal readonly struct ArrayDimensionsEqualAssertionError(System.Collections.IEnumerable expectedValue, System.Collections.IEnumerable actualValue, string? message, string? actualExpression, string? expectedExpression)
{
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public System.Collections.IEnumerable ExpectedValue { get; } = expectedValue;
    public System.Collections.IEnumerable ActualValue { get; } = actualValue;
}
