namespace Meziantou.Framework.Assertions;

internal readonly struct NegativeReadOnlySpanExpectedActualValueAssertionError<TExpected, TActual>(string assertionName, string notExpectedLabel, TExpected expectedValue, ReadOnlySpan<TActual> actualValue, string? actualExpression, string? expectedExpression, string? message)
{
    public string AssertionName { get; } = assertionName;
    public string NotExpectedLabel { get; } = notExpectedLabel;
    public TExpected ExpectedValue { get; } = expectedValue;
    public ReadOnlyMemory<TActual> ActualValue { get; } = actualValue.ToArray();
    public string? ActualExpression { get; } = actualExpression;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? Message { get; } = message;
}
