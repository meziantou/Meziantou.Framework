namespace Meziantou.Framework.Assertions;

internal readonly ref struct NullExpectedAssertionError<TActual>(string assertionName, TActual actualValue, string? actualExpression, string? expectedExpression, string? message = null)
{
    public string? Message { get; } = message;
    public string AssertionName { get; } = assertionName;
    public TActual ActualValue { get; } = actualValue;
    public string? ActualExpression { get; } = actualExpression;
    public string? ExpectedExpression { get; } = expectedExpression;
}
