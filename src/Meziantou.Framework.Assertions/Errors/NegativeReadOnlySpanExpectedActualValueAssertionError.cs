namespace Meziantou.Framework.Assertions;

internal readonly ref struct NegativeReadOnlySpanExpectedActualValueAssertionError<TExpected, TActual>(string assertionName, string notExpectedLabel, TExpected expectedValue, ReadOnlySpan<TActual> actualValue, string? actualExpression, string? expectedExpression, string? message)
{
    public string AssertionName { get; } = assertionName;
    public string NotExpectedLabel { get; } = notExpectedLabel;
    public TExpected ExpectedValue { get; } = expectedValue;
    public ReadOnlySpan<TActual> ActualValue { get; } = actualValue;
    public string? ActualExpression { get; } = actualExpression;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? Message { get; } = message;
}
