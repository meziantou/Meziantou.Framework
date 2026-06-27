namespace Meziantou.Framework.Assertions;

internal readonly struct NegativeActualValueAssertionError<TActual>(string assertionName, string notExpectedText, TActual actualValue, string? actualExpression, string? message)
{
    public string AssertionName { get; } = assertionName;
    public string NotExpectedText { get; } = notExpectedText;
    public TActual ActualValue { get; } = actualValue;
    public string? ActualExpression { get; } = actualExpression;
    public string? Message { get; } = message;
}
