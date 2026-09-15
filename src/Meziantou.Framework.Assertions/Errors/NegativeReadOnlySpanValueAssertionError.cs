namespace Meziantou.Framework.Assertions;

internal readonly ref struct NegativeReadOnlySpanValueAssertionError<TExpected, TActual>(string assertionName, string notExpectedLabel, ReadOnlySpan<TExpected> expectedValue, ReadOnlySpan<TActual> actualValue, string? actualExpression, string? expectedExpression, string? message)
{
    public string AssertionName { get; } = assertionName;
    public string NotExpectedLabel { get; } = notExpectedLabel;
    public string? Message { get; } = message;
    public string? ExpectedExpression { get; } = expectedExpression;
    public string? ActualExpression { get; } = actualExpression;
    public ReadOnlySpan<TExpected> ExpectedValue { get; } = expectedValue;
    public ReadOnlySpan<TActual> ActualValue { get; } = actualValue;
}
