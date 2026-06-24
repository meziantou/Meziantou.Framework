namespace Meziantou.Framework.Assertions;

internal readonly struct NegativeValueAssertionError<TExpected, TActual>(string assertionName, string notExpectedLabel, TExpected expectedValue, TActual actualValue, string? actualExpression, string? expectedExpression, string? message)
{
    public string AssertionName { get; } = assertionName;
    public string NotExpectedLabel { get; } = notExpectedLabel;
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public TExpected ExpectedValue { get; } = expectedValue;
    public TActual ActualValue { get; } = actualValue;
}
