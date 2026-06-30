namespace Meziantou.Framework.Assertions;

internal readonly struct NegativeSetAssertionError(System.Collections.IEnumerable expectedValue, System.Collections.IEnumerable actualValue, bool isSuperset, string? actualExpression, string? expectedExpression, string? message = null)
{
    public string? Message { get; } = message;
    public System.Collections.IEnumerable ExpectedValue { get; } = expectedValue;
    public System.Collections.IEnumerable ActualValue { get; } = actualValue;
    public bool IsSuperset { get; } = isSuperset;
    public string? ActualExpression { get; } = actualExpression;
    public string? ExpectedExpression { get; } = expectedExpression;
}
