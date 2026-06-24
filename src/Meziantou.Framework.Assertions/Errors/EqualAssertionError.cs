namespace Meziantou.Framework.Assertions;

internal readonly ref struct EqualAssertionError<TExpected, TActual>(TExpected expectedValue, TActual actualValue, string? message, string? actualExpression, string? expectedExpression)
{
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public TExpected ExpectedValue { get; } = expectedValue;
    public TActual ActualValue { get; } = actualValue;
}
