namespace Meziantou.Framework.Assertions;

internal readonly ref struct DictionaryContainsAssertionError(object? expectedKey, System.Collections.IDictionary actualValue, string? actualExpression, string? expectedExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string? ActualExpression { get; } = actualExpression;
    public string? ExpectedExpression { get; } = expectedExpression;
    public object? ExpectedKey { get; } = expectedKey;
    public System.Collections.IDictionary ActualValue { get; } = actualValue;
}
