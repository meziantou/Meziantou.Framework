namespace Meziantou.Framework.Assertions;

internal readonly struct EqualByStructureAssertionError(string assertionName, object? expectedValue, object? actualValue, string path, string reason, string? message, string? actualExpression, string? expectedExpression)
{
    public string AssertionName { get; } = assertionName;
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public string Path { get; } = path;
    public string Reason { get; } = reason;
    public object? ExpectedValue { get; } = expectedValue;
    public object? ActualValue { get; } = actualValue;
}
