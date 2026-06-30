namespace Meziantou.Framework.Assertions;

internal readonly struct NegativeTypeAssertionError(string assertionName, string notExpectedTypeLabel, Type expectedType, object? actualValue, string? actualExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string AssertionName { get; } = assertionName;
    public string NotExpectedTypeLabel { get; } = notExpectedTypeLabel;
    public Type ExpectedType { get; } = expectedType;
    public object? ActualValue { get; } = actualValue;
    public string? ActualExpression { get; } = actualExpression;
}
